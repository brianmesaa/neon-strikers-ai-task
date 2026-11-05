using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NeonStrikers.Arenas;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NeonStrikers.Tests
{
    /// <summary>
    /// Static checks of the arena editor wiring: menu button, build settings, the CustomArena scene
    /// (a copy of Arena_01 whose objects are the templates of every editor object) and the editor scene.
    /// </summary>
    public class ArenaEditorSceneWiringTests
    {
        const string MainMenuPath = "Assets/Scenes/Main Menu.unity";
        const string Arena01Path = "Assets/Scenes/Arena_01.unity";
        const string CustomArenaPath = "Assets/Scenes/CustomArena.unity";
        const string EditorPath = "Assets/Scenes/ArenaEditor.unity";

        readonly List<Scene> openedScenes = new List<Scene>();

        [TearDown]
        public void CloseScenes()
        {
            foreach (Scene scene in openedScenes)
            {
                if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(scene, true);
            }

            openedScenes.Clear();
        }

        [Test]
        public void MainMenuHasAnArenaEditorButtonNextToTheGameModes()
        {
            Scene scene = Open(MainMenuPath);
            Button editor = FindComponent<Button>(scene, "ArenaEditorButton");

            Assert.IsTrue(CallsMethod(editor, "SceneLoader", "OpenArenaEditor"));
            StringAssert.Contains("ARENA EDITOR", editor.GetComponentInChildren<TMP_Text>(true).text);
            Assert.IsTrue(editor.gameObject.activeSelf && editor.interactable);

            Assert.IsTrue(CallsMethod(FindComponent<Button>(scene, "TwoPlayersButton"), "SceneLoader", "StartTwoPlayers"));
            Assert.IsTrue(CallsMethod(FindComponent<Button>(scene, "VsComputerButton"), "SceneLoader", "StartVsComputer"));
            Assert.IsTrue(CallsMethod(FindComponent<Button>(scene, "QuitButton"), "SceneLoader", "QuitGame"));

            // The buttons must not overlap.
            float[] ys = new[] { "TwoPlayersButton", "VsComputerButton", "ArenaEditorButton" }
                .Select(n => FindComponent<RectTransform>(scene, n).anchoredPosition.y).ToArray();
            for (int i = 1; i < ys.Length; i++)
                Assert.GreaterOrEqual(ys[i - 1] - ys[i], 64f);
            Assert.Less(FindComponent<RectTransform>(scene, "QuitButton").anchoredPosition.y, ys[2] - 64f);
        }

        [Test]
        public void BuildSettingsContainTheEditorAndCustomArenaScenesBeforeResults()
        {
            List<string> enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            CollectionAssert.Contains(enabled, CustomArenaPath);
            CollectionAssert.Contains(enabled, EditorPath);
            Assert.AreEqual("Assets/Scenes/Results.unity", enabled.Last(), "Results stays the last scene");
            Assert.Less(enabled.IndexOf("Assets/Scenes/Arena_03.unity"), enabled.IndexOf(CustomArenaPath));
        }

        [Test]
        public void EditorSceneRunsTheInGameEditor()
        {
            Scene scene = Open(EditorPath);
            Assert.IsTrue(AllComponents<MonoBehaviour>(scene).Any(c => c != null && c.GetType().Name == "ArenaEditorController"));
            Assert.IsTrue(AllComponents<Camera>(scene).Any());
        }

        [Test]
        public void CustomArenaSceneContainsAllTemplatesWithArena01Values()
        {
            Scene custom = Open(CustomArenaPath);
            Scene arena01 = Open(Arena01Path);

            foreach (string path in new[]
                     {
                         "Walls/BottomLeftWall", "ArenaPlatforms/BottomPlatform", "Portals/LeftPortal",
                         "Goals/LeftGoal", "Goals/RightGoal", "Goals/LeftGoalSlowZone"
                     })
            {
                Transform template = FindPath(custom, path);
                Transform reference = FindPath(arena01, path);
                Assert.IsNotNull(template, "CustomArena is missing template " + path);
                Assert.IsNotNull(reference, "Arena_01 is missing " + path);

                foreach (Component component in reference.GetComponents<Component>())
                {
                    if (component is Transform)
                        continue;

                    Component twin = template.GetComponent(component.GetType());
                    Assert.IsNotNull(twin, path + " lacks " + component.GetType().Name);
                    AssertSameSerializedValues(component, twin, path);
                }
            }

            Assert.AreEqual("Red", Serialized(FindPath(custom, "Goals/LeftGoal"), "GoalZone", "scoringTeam").stringValue);
            Assert.AreEqual("Blue", Serialized(FindPath(custom, "Goals/RightGoal"), "GoalZone", "scoringTeam").stringValue);

            MonoBehaviour manager = AllComponents<MonoBehaviour>(custom).First(c => c != null && c.GetType().Name == "GameManager");
            Assert.IsNotNull(new SerializedObject(manager).FindProperty("ball").objectReferenceValue);
            Assert.IsTrue(AllComponents<MonoBehaviour>(custom).Any(c => c != null && c.GetType().Name == "SceneLoader"));
        }

        [Test]
        public void CustomArenaPlayersAndBallMatchTheStockArenas()
        {
            Scene custom = Open(CustomArenaPath);
            Scene arena01 = Open(Arena01Path);

            foreach (string root in new[] { "Player", "Player2", "Ball", "GameManager", "Main Camera" })
            {
                GameObject a = custom.GetRootGameObjects().First(g => g.name == root);
                GameObject b = arena01.GetRootGameObjects().First(g => g.name == root);
                foreach (Component component in b.GetComponents<Component>())
                {
                    if (component is Transform)
                        continue;
                    AssertSameSerializedValues(component, a.GetComponent(component.GetType()), root);
                }
            }
        }

        [Test]
        public void GameplayHooksExist()
        {
            Type loader = FindGameplayType("SceneLoader");
            Assert.IsNotNull(loader.GetMethod("OpenArenaEditor", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(loader.GetMethod("ReturnToArenaEditor", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(FindGameplayType("CustomArenaBuilder"));
            Assert.IsNotNull(FindGameplayType("ArenaEditorController"));
            Assert.AreEqual("CustomArena", CustomArenaFlow.CustomArenaScene);
            Assert.AreEqual("ArenaEditor", CustomArenaFlow.EditorScene);
        }

        // ---------------------------------------------------------------- helpers

        Scene Open(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            openedScenes.Add(scene);
            Assert.IsTrue(scene.IsValid(), "could not open " + path);
            return scene;
        }

        static IEnumerable<T> AllComponents<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
        }

        static T FindComponent<T>(Scene scene, string objectName) where T : Component
        {
            T found = AllComponents<T>(scene).FirstOrDefault(c => c.gameObject.name == objectName);
            Assert.IsNotNull(found, $"{scene.name}: no {typeof(T).Name} on '{objectName}'");
            return found;
        }

        static Transform FindPath(Scene scene, string path)
        {
            string[] parts = path.Split('/');
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
            if (root == null)
                return null;
            return parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1)));
        }

        static SerializedProperty Serialized(Transform t, string componentType, string property)
        {
            Component component = t.GetComponent(componentType);
            Assert.IsNotNull(component, t.name + " has no " + componentType);
            return new SerializedObject(component).FindProperty(property);
        }

        static bool CallsMethod(Button button, string targetType, string method)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                UnityEngine.Object target = button.onClick.GetPersistentTarget(i);
                if (target != null && target.GetType().Name == targetType && button.onClick.GetPersistentMethodName(i) == method)
                    return true;
            }

            return false;
        }

        /// <summary>Compares every serialized value except object references (scene-local ids differ between scenes).</summary>
        static void AssertSameSerializedValues(Component expected, Component actual, string context)
        {
            Assert.IsNotNull(actual, context + ": missing " + expected.GetType().Name);
            var a = new SerializedObject(expected);
            var b = new SerializedObject(actual);
            SerializedProperty it = a.GetIterator();
            bool enterChildren = true;
            while (it.Next(enterChildren))
            {
                enterChildren = true;
                if (it.propertyPath == "m_GameObject" || it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    enterChildren = false;
                    continue;
                }

                SerializedProperty other = b.FindProperty(it.propertyPath);
                Assert.IsNotNull(other, context + ": " + it.propertyPath);
                if (!it.hasChildren && it.propertyType != SerializedPropertyType.Generic)
                    Assert.IsTrue(SerializedProperty.DataEquals(it, other), context + " " + expected.GetType().Name + "." + it.propertyPath + " differs from Arena_01");
            }
        }

        static Type FindGameplayType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Assembly-CSharp")
                .Select(a => a.GetType(name))
                .FirstOrDefault(t => t != null);

            Assert.IsNotNull(type, name + " not found in Assembly-CSharp");
            return type;
        }
    }
}
