// filters the fields of all components of a GameObject based on a user-provided string
// matching against both field names and types, with a UI to input the filter and visual highlights for matched fields.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityLibrary.EditorTools
{
    [InitializeOnLoad]
    public static class InspectorFilter
    {
        private static readonly Dictionary<int, string> FiltersByGameObjectId = new Dictionary<int, string>();
        private static readonly Dictionary<int, List<string>> MatchedFieldsByComponentId = new Dictionary<int, List<string>>();

        static InspectorFilter()
        {
            Editor.finishedDefaultHeaderGUI += OnFinishedDefaultHeaderGUI;
            Selection.selectionChanged += ApplyFilterForCurrentSelection;
            Undo.undoRedoPerformed += ApplyFilterForCurrentSelection;
            EditorApplication.delayCall += ApplyFilterForCurrentSelection;
        }

        internal static bool TryGetFilterForGameObject(GameObject go, out string filter)
        {
            filter = string.Empty;
            if (go == null)
            {
                return false;
            }

            if (!FiltersByGameObjectId.TryGetValue(go.GetInstanceID(), out string value) || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            filter = value.Trim();
            return filter.Length > 0;
        }

        internal static bool IsPropertyMatch(SerializedProperty property, string filter)
        {
            if (property == null || string.IsNullOrWhiteSpace(filter))
            {
                return false;
            }

            if (property.propertyPath == "m_Script")
            {
                return false;
            }

            return property.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || property.displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void OnFinishedDefaultHeaderGUI(Editor editor)
        {
            if (editor.target is GameObject go)
            {
                DrawGameObjectFilterUI(go);
                return;
            }

            if (!(editor.target is Component component))
            {
                return;
            }

            int gameObjectId = component.gameObject.GetInstanceID();
            if (!FiltersByGameObjectId.TryGetValue(gameObjectId, out string filter) || string.IsNullOrWhiteSpace(filter))
            {
                return;
            }

            if (!MatchedFieldsByComponentId.TryGetValue(component.GetInstanceID(), out List<string> matchedFields) || matchedFields.Count == 0)
            {
                return;
            }

            Color old = GUI.color;
            GUI.color = new Color(1f, 0.95f, 0.55f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.color = old;
            EditorGUILayout.LabelField("Matched fields: " + string.Join(", ", matchedFields), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private static void DrawGameObjectFilterUI(GameObject go)
        {
            int id = go.GetInstanceID();
            FiltersByGameObjectId.TryGetValue(id, out string currentFilter);
            currentFilter ??= string.Empty;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            const string filterControlName = "InspectorFilter_FilterField";
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName(filterControlName);
            string newFilter = EditorGUILayout.TextField("Filter", currentFilter);
            bool clearClicked = GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20f));
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndHorizontal();

            Event e = Event.current;
            bool escapePressed = e.type == EventType.KeyDown
                && e.keyCode == KeyCode.Escape
                && GUI.GetNameOfFocusedControl() == filterControlName;

            if (clearClicked || escapePressed)
            {
                newFilter = string.Empty;
                changed = true;
                GUI.FocusControl(null);
                if (escapePressed)
                {
                    e.Use();
                }
            }

            if (changed)
            {
                if (string.IsNullOrWhiteSpace(newFilter))
                {
                    FiltersByGameObjectId.Remove(id);
                    newFilter = string.Empty;
                }
                else
                {
                    FiltersByGameObjectId[id] = newFilter;
                }

                ApplyFilter(go, newFilter);
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }
            else
            {
                ApplyFilter(go, currentFilter);
            }

            EditorGUILayout.EndVertical();
        }

        private static void ApplyFilterForCurrentSelection()
        {
            if (!(Selection.activeGameObject is GameObject go))
            {
                return;
            }

            int id = go.GetInstanceID();
            FiltersByGameObjectId.TryGetValue(id, out string filter);
            ApplyFilter(go, filter);
            ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        private static void ApplyFilter(GameObject go, string filter)
        {
            ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
            Editor[] editors = tracker.activeEditors;
            if (editors == null || editors.Length == 0)
            {
                return;
            }

            bool hasFilter = !string.IsNullOrWhiteSpace(filter);
            string normalizedFilter = hasFilter ? filter.Trim() : string.Empty;

            for (int i = 0; i < editors.Length; i++)
            {
                UnityEngine.Object target = editors[i].target;
                if (!(target is Component component) || component.gameObject != go)
                {
                    tracker.SetVisible(i, 1);
                    continue;
                }

                int componentId = component.GetInstanceID();

                if (!hasFilter)
                {
                    MatchedFieldsByComponentId.Remove(componentId);
                    tracker.SetVisible(i, 1);
                    continue;
                }

                bool typeMatch = component.GetType().Name.IndexOf(normalizedFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                List<string> fieldMatches = GetMatchingSerializedFields(editors[i], normalizedFilter);
                bool fieldMatch = fieldMatches.Count > 0;

                if (fieldMatch)
                {
                    MatchedFieldsByComponentId[componentId] = fieldMatches;
                }
                else
                {
                    MatchedFieldsByComponentId.Remove(componentId);
                }

                tracker.SetVisible(i, (typeMatch || fieldMatch) ? 1 : 0);
            }
        }

        private static List<string> GetMatchingSerializedFields(Editor editor, string filter)
        {
            List<string> matches = new List<string>();

            SerializedObject serializedObject = editor.serializedObject;
            if (serializedObject == null)
            {
                return matches;
            }

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (!IsPropertyMatch(iterator, filter))
                {
                    continue;
                }

                string label = iterator.displayName;
                if (!matches.Contains(label))
                {
                    matches.Add(label);
                }
            }

            return matches;
        }
    }

    [CustomEditor(typeof(Component), true, isFallback = true)]
    [CanEditMultipleObjects]
    public class InspectorFilterComponentEditor : Editor
    {
        private static readonly Color HighlightColor = new Color(0.5058824f, 0.7058824f, 1f, 1f);

        public override void OnInspectorGUI()
        {
            Component component = target as Component;
            if (component == null || !InspectorFilter.TryGetFilterForGameObject(component.gameObject, out string filter))
            {
                DrawDefaultInspector();
                return;
            }

            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                {
                    float height = EditorGUI.GetPropertyHeight(property, true);
                    Rect rect = EditorGUILayout.GetControlRect(true, height);

                    if (InspectorFilter.IsPropertyMatch(property, filter))
                    {
                        DrawBorder(rect, HighlightColor, 1f);
                    }

                    EditorGUI.PropertyField(rect, property, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }
    }

}
