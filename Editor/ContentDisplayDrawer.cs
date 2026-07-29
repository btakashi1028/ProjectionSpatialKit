using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Draws a 0-based display index as the display NUMBER the user sees elsewhere
    /// ("Display 1" == index 0), so the Inspector never asks anyone to mentally convert.
    /// </summary>
    [CustomPropertyDrawer(typeof(ContentDisplayAttribute))]
    public sealed class ContentDisplayDrawer : PropertyDrawer
    {
        private static readonly string[] Names =
        {
            "Display 1", "Display 2", "Display 3", "Display 4",
            "Display 5", "Display 6", "Display 7", "Display 8"
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            int index = Mathf.Clamp(property.intValue, 0, Names.Length - 1);
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(position, label.text, index, Names);
            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = picked;
            }
            EditorGUI.EndProperty();
        }
    }
}
