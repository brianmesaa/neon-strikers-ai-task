using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NeonStrikers.Arenas
{
    /// <summary>
    /// The arena being edited: data + selection + undo/redo history.
    ///
    /// Every change goes through this class so it can be undone. History entries are full snapshots of the
    /// arena (objects are small), which means relationships (portal links) are restored exactly as they were:
    /// undoing a deletion brings back the object AND the links of its partner.
    ///
    /// Object references must not be kept across operations (undo replaces them); look objects up by id.
    /// </summary>
    public sealed class ArenaEditorDocument
    {
        public const int MaxHistory = 200;

        sealed class Snapshot
        {
            public ArenaData Data;
            public List<int> Selection;
            public int StateId;
            public string Label;
            public string MergeKey;
        }

        readonly List<Snapshot> undoStack = new List<Snapshot>();
        readonly List<Snapshot> redoStack = new List<Snapshot>();
        readonly List<int> selection = new List<int>();

        int stateCounter;
        int currentStateId;
        int savedStateId;

        Snapshot pendingInteraction;
        bool interactionChanged;

        public ArenaData Data { get; private set; }

        /// <summary>Increases on every change, including undo / redo (used to refresh validation).</summary>
        public int Revision { get; private set; }

        public IReadOnlyList<int> Selection => selection;
        public int PrimarySelection => selection.Count > 0 ? selection[selection.Count - 1] : 0;

        public bool CanUndo => undoStack.Count > 0 && pendingInteraction == null;
        public bool CanRedo => redoStack.Count > 0 && pendingInteraction == null;
        public string UndoLabel => undoStack.Count > 0 ? undoStack[undoStack.Count - 1].Label : "";
        public string RedoLabel => redoStack.Count > 0 ? redoStack[redoStack.Count - 1].Label : "";
        public int UndoCount => undoStack.Count;
        public int RedoCount => redoStack.Count;

        /// <summary>True when the arena differs from the last saved (or opened) version.</summary>
        public bool IsDirty => currentStateId != savedStateId;

        public bool IsInteracting => pendingInteraction != null;

        /// <summary>The arena has never been written to disk.</summary>
        public bool IsNew => string.IsNullOrEmpty(Data.id);

        public event Action Changed;

        public ArenaEditorDocument(ArenaData data, bool markSaved)
        {
            Data = data != null ? data.Clone() : ArenaFactory.CreateStarter();
            if (Data.objects == null)
                Data.objects = new List<ArenaObjectData>();
            Data.objects.RemoveAll(o => o == null);

            stateCounter = 1;
            currentStateId = 1;
            savedStateId = markSaved ? 1 : 0;
        }

        public static ArenaEditorDocument CreateNew(bool starterLayout, string name = ArenaData.DefaultName)
        {
            ArenaData data = starterLayout ? ArenaFactory.CreateStarter(name) : ArenaFactory.CreateEmpty(name);
            return new ArenaEditorDocument(data, false);
        }

        public ArenaObjectData Find(int id)
        {
            return Data.Find(id);
        }

        public bool Exists(int id)
        {
            return Data.Find(id) != null;
        }

        // ------------------------------------------------------------------ selection

        public bool IsSelected(int id)
        {
            return selection.Contains(id);
        }

        public void Select(int id)
        {
            selection.Clear();
            if (Exists(id))
                selection.Add(id);
        }

        public void SetSelection(IEnumerable<int> ids)
        {
            selection.Clear();
            if (ids == null)
                return;

            foreach (int id in ids)
            {
                if (Exists(id) && !selection.Contains(id))
                    selection.Add(id);
            }
        }

        public void AddToSelection(int id)
        {
            if (Exists(id) && !selection.Contains(id))
                selection.Add(id);
        }

        public void ToggleSelection(int id)
        {
            if (selection.Contains(id))
                selection.Remove(id);
            else
                AddToSelection(id);
        }

        public void ClearSelection()
        {
            selection.Clear();
        }

        public void SelectAll()
        {
            SetSelection(Data.objects.Select(o => o.id));
        }

        public List<ArenaObjectData> SelectedObjects()
        {
            var result = new List<ArenaObjectData>();
            foreach (int id in selection)
            {
                ArenaObjectData obj = Find(id);
                if (obj != null)
                    result.Add(obj);
            }

            return result;
        }

        // ------------------------------------------------------------------ history core

        Snapshot Capture(string label, string mergeKey)
        {
            return new Snapshot
            {
                Data = Data.Clone(),
                Selection = new List<int>(selection),
                StateId = currentStateId,
                Label = label,
                MergeKey = mergeKey
            };
        }

        void PushUndo(Snapshot snapshot)
        {
            undoStack.Add(snapshot);
            if (undoStack.Count > MaxHistory)
                undoStack.RemoveAt(0);
            redoStack.Clear();
        }

        void Record(string label, string mergeKey = null)
        {
            EndInteraction();

            bool merge = !string.IsNullOrEmpty(mergeKey) && undoStack.Count > 0 && redoStack.Count == 0 &&
                         undoStack[undoStack.Count - 1].MergeKey == mergeKey;

            if (!merge)
                PushUndo(Capture(label, mergeKey));
        }

        void Commit()
        {
            currentStateId = ++stateCounter;
            Revision++;
            Changed?.Invoke();
        }

        /// <summary>Breaks the merge chain so the next edit with the same merge key creates its own undo step.</summary>
        public void BreakMerge()
        {
            if (undoStack.Count > 0)
                undoStack[undoStack.Count - 1].MergeKey = null;
        }

        /// <summary>
        /// Generic undoable change. Consecutive changes with the same non-empty mergeKey (e.g. typing into a
        /// number field) become a single undo step.
        /// </summary>
        public void Modify(string label, Action<ArenaData> change, string mergeKey = null)
        {
            if (change == null)
                return;

            Record(label, mergeKey);
            change(Data);
            selection.RemoveAll(id => !Exists(id));
            Commit();
        }

        /// <summary>Start of a continuous edit (dragging, resizing, rotating). Ends with <see cref="EndInteraction"/>.</summary>
        public void BeginInteraction(string label)
        {
            EndInteraction();
            pendingInteraction = Capture(label, null);
            interactionChanged = false;
        }

        /// <summary>Apply a step of the current interaction (not recorded until the interaction ends).</summary>
        public void UpdateInteraction(Action<ArenaData> change)
        {
            if (pendingInteraction == null || change == null)
                return;

            change(Data);
            interactionChanged = true;
            Revision++;
        }

        /// <summary>Commits the interaction as one undo step (if it changed anything).</summary>
        public void EndInteraction()
        {
            if (pendingInteraction == null)
                return;

            Snapshot snapshot = pendingInteraction;
            pendingInteraction = null;

            if (!interactionChanged)
                return;

            interactionChanged = false;
            PushUndo(snapshot);
            Commit();
        }

        /// <summary>Reverts the current interaction.</summary>
        public void CancelInteraction()
        {
            if (pendingInteraction == null)
                return;

            Restore(pendingInteraction);
            pendingInteraction = null;
            interactionChanged = false;
            Revision++;
        }

        void Restore(Snapshot snapshot)
        {
            int keepNextId = Mathf.Max(Data.nextObjectId, snapshot.Data.nextObjectId);
            Data = snapshot.Data.Clone();
            Data.nextObjectId = keepNextId;
            selection.Clear();
            foreach (int id in snapshot.Selection)
            {
                if (Exists(id))
                    selection.Add(id);
            }

            currentStateId = snapshot.StateId;
        }

        public bool Undo()
        {
            EndInteraction();
            if (undoStack.Count == 0)
                return false;

            Snapshot target = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            redoStack.Add(Capture(target.Label, null));
            Restore(target);
            Revision++;
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            EndInteraction();
            if (redoStack.Count == 0)
                return false;

            Snapshot target = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            undoStack.Add(Capture(target.Label, null));
            Restore(target);
            Revision++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Call after the arena was written to disk (the data may have received an id).</summary>
        public void MarkSaved(ArenaData savedData = null)
        {
            if (savedData != null)
            {
                Data.id = savedData.id;
                Data.name = savedData.name;
                Data.createdUtc = savedData.createdUtc;
                Data.modifiedUtc = savedData.modifiedUtc;
                Data.formatVersion = savedData.formatVersion;

                // Saved identity applies to every history state, so undo can't "unsave" the file id.
                foreach (Snapshot s in undoStack.Concat(redoStack))
                {
                    s.Data.id = savedData.id;
                    s.Data.createdUtc = savedData.createdUtc;
                }
            }

            savedStateId = currentStateId;
            Revision++;
            Changed?.Invoke();
        }

        /// <summary>The arena file was deleted: keep editing, but as a new, unsaved arena.</summary>
        public void ForgetFile()
        {
            Data.id = "";
            foreach (Snapshot s in undoStack.Concat(redoStack))
                s.Data.id = "";
            savedStateId = -1;
            Revision++;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ editing operations

        public void Rename(string newName)
        {
            if (newName == Data.name)
                return;

            Modify("Rename arena", d => d.name = newName, "rename");
        }

        /// <summary>Adds a new object at the given position and selects it.</summary>
        public ArenaObjectData Add(ArenaObjectType kind, Vector2 position)
        {
            if (kind == ArenaObjectType.Unknown)
                return null;

            int newId = 0;
            Modify("Add " + ArenaRules.DisplayName(kind), d =>
            {
                ArenaObjectData obj = ArenaFactory.Create(kind, d.AllocateId(), position);
                d.objects.Add(obj);
                newId = obj.id;
            });

            Select(newId);
            return Find(newId);
        }

        /// <summary>
        /// Ball and spawns exist once: placing one moves the existing object (if any) instead of adding another.
        /// Other types are simply added.
        /// </summary>
        public ArenaObjectData Place(ArenaObjectType kind, Vector2 position)
        {
            if (!ArenaRules.IsUniqueType(kind))
                return Add(kind, position);

            ArenaObjectData existing = Data.FirstOf(kind);
            if (existing == null)
                return Add(kind, position);

            int id = existing.id;
            Modify("Move " + ArenaRules.DisplayName(kind), d =>
            {
                ArenaObjectData obj = d.Find(id);
                obj.x = position.x;
                obj.y = position.y;
            });

            Select(id);
            return Find(id);
        }

        /// <summary>Adds two portals that are connected to each other and selects both.</summary>
        public List<ArenaObjectData> AddPortalPair(Vector2 first, Vector2 second, float firstRotation = 0f, float secondRotation = 0f)
        {
            int a = 0;
            int b = 0;
            Modify("Add portal pair", d =>
            {
                ArenaObjectData pa = ArenaFactory.Create(ArenaObjectType.Portal, d.AllocateId(), first);
                ArenaObjectData pb = ArenaFactory.Create(ArenaObjectType.Portal, d.AllocateId(), second);
                pa.rotation = NormalizeAngle(firstRotation);
                pb.rotation = NormalizeAngle(secondRotation);
                pa.linkedPortalId = pb.id;
                pb.linkedPortalId = pa.id;
                d.objects.Add(pa);
                d.objects.Add(pb);
                a = pa.id;
                b = pb.id;
            });

            SetSelection(new[] { a, b });
            return new List<ArenaObjectData> { Find(a), Find(b) };
        }

        /// <summary>
        /// Deletes objects. Portals that stay but were linked to a deleted portal become unpaired.
        /// Undo restores the deleted objects and the links on both sides.
        /// </summary>
        public int Delete(IEnumerable<int> ids)
        {
            var targets = new HashSet<int>((ids ?? Enumerable.Empty<int>()).Where(Exists));
            if (targets.Count == 0)
                return 0;

            Modify(targets.Count == 1 ? "Delete " + ArenaRules.DisplayName(Find(targets.First()).Kind) : "Delete " + targets.Count + " objects", d =>
            {
                d.objects.RemoveAll(o => targets.Contains(o.id));
                foreach (ArenaObjectData obj in d.objects)
                {
                    if (obj.IsPortal && targets.Contains(obj.linkedPortalId))
                        obj.linkedPortalId = 0;
                }
            });

            return targets.Count;
        }

        public int DeleteSelection()
        {
            return Delete(new List<int>(selection));
        }

        /// <summary>
        /// Duplicates objects (offset by <paramref name="offset"/>) and selects the copies.
        /// A portal copy is linked to the copy of its partner when the partner is duplicated too; otherwise
        /// the copy is unpaired. The originals are never modified.
        /// </summary>
        public List<ArenaObjectData> Duplicate(IEnumerable<int> ids, Vector2 offset)
        {
            List<int> sources = (ids ?? Enumerable.Empty<int>()).Where(Exists).Distinct().ToList();
            if (sources.Count == 0)
                return new List<ArenaObjectData>();

            var copies = new List<int>();
            Modify(sources.Count == 1 ? "Duplicate " + ArenaRules.DisplayName(Find(sources[0]).Kind) : "Duplicate " + sources.Count + " objects", d =>
            {
                var map = new Dictionary<int, ArenaObjectData>();
                foreach (int sourceId in sources)
                {
                    ArenaObjectData copy = d.Find(sourceId).Clone();
                    copy.id = d.AllocateId();
                    copy.x += offset.x;
                    copy.y += offset.y;
                    map[sourceId] = copy;
                }

                foreach (KeyValuePair<int, ArenaObjectData> pair in map)
                {
                    ArenaObjectData copy = pair.Value;
                    if (!copy.IsPortal)
                        continue;

                    copy.linkedPortalId = copy.linkedPortalId != 0 && map.TryGetValue(copy.linkedPortalId, out ArenaObjectData partnerCopy)
                        ? partnerCopy.id
                        : 0;
                }

                foreach (int sourceId in sources)
                {
                    d.objects.Add(map[sourceId]);
                    copies.Add(map[sourceId].id);
                }
            });

            SetSelection(copies);
            return copies.Select(Find).ToList();
        }

        public List<ArenaObjectData> DuplicateSelection(Vector2 offset)
        {
            return Duplicate(new List<int>(selection), offset);
        }

        /// <summary>Connects two portals to each other. Their previous partners (if any) become unpaired.</summary>
        public bool Link(int firstId, int secondId)
        {
            ArenaObjectData first = Find(firstId);
            ArenaObjectData second = Find(secondId);
            if (first == null || second == null || firstId == secondId || !first.IsPortal || !second.IsPortal)
                return false;

            if (first.linkedPortalId == secondId && second.linkedPortalId == firstId)
                return true;

            Modify("Link portals", d =>
            {
                ArenaObjectData a = d.Find(firstId);
                ArenaObjectData b = d.Find(secondId);
                UnlinkInternal(d, a);
                UnlinkInternal(d, b);
                a.linkedPortalId = b.id;
                b.linkedPortalId = a.id;
            });

            return true;
        }

        public bool Unlink(int portalId)
        {
            ArenaObjectData portal = Find(portalId);
            if (portal == null || !portal.IsPortal || portal.linkedPortalId == 0)
                return false;

            Modify("Unlink portal", d => UnlinkInternal(d, d.Find(portalId)));
            return true;
        }

        static void UnlinkInternal(ArenaData d, ArenaObjectData portal)
        {
            if (portal == null)
                return;

            ArenaObjectData partner = d.Find(portal.linkedPortalId);
            if (partner != null && partner.IsPortal && partner.linkedPortalId == portal.id)
                partner.linkedPortalId = 0;

            portal.linkedPortalId = 0;
        }

        /// <summary>Partner of a portal when the link is valid and mutual, otherwise null.</summary>
        public ArenaObjectData PartnerOf(ArenaObjectData portal)
        {
            if (portal == null || !portal.IsPortal || portal.linkedPortalId == 0)
                return null;

            ArenaObjectData partner = Find(portal.linkedPortalId);
            if (partner == null || !partner.IsPortal || partner.linkedPortalId != portal.id)
                return null;

            return partner;
        }

        /// <summary>Changes a goal's team (Blue goal &lt;-&gt; Red goal).</summary>
        public void SwapGoalTeam(int goalId)
        {
            ArenaObjectData goal = Find(goalId);
            if (goal == null || !goal.IsGoal)
                return;

            Modify("Swap goal team", d =>
            {
                ArenaObjectData g = d.Find(goalId);
                g.Kind = g.Kind == ArenaObjectType.BlueGoal ? ArenaObjectType.RedGoal : ArenaObjectType.BlueGoal;
            });
        }

        public void MoveBy(IEnumerable<int> ids, Vector2 delta, string label = "Move")
        {
            List<int> targets = (ids ?? Enumerable.Empty<int>()).Where(Exists).ToList();
            if (targets.Count == 0 || delta == Vector2.zero)
                return;

            Modify(label, d =>
            {
                foreach (int id in targets)
                {
                    ArenaObjectData obj = d.Find(id);
                    obj.x += delta.x;
                    obj.y += delta.y;
                }
            });
        }

        public void RotateBy(IEnumerable<int> ids, float degrees)
        {
            List<int> targets = (ids ?? Enumerable.Empty<int>()).Where(id => Exists(id) && ArenaRules.IsRotatable(Find(id).Kind)).ToList();
            if (targets.Count == 0)
                return;

            Modify("Rotate", d =>
            {
                foreach (int id in targets)
                {
                    ArenaObjectData obj = d.Find(id);
                    obj.rotation = NormalizeAngle(obj.rotation + degrees);
                }
            });
        }

        public static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        /// <summary>Clamp a size to the allowed range and keep circles round.</summary>
        public static Vector2 ClampSize(ArenaObjectType kind, Vector2 size)
        {
            float w = Mathf.Clamp(size.x, ArenaRules.MinSize, ArenaRules.MaxSize);
            float h = Mathf.Clamp(size.y, ArenaRules.MinSize, ArenaRules.MaxSize);
            if (ArenaRules.IsCircular(kind))
                h = w;
            return new Vector2(w, h);
        }
    }
}
