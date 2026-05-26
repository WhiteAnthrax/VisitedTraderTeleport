using UnityEngine;

namespace VisitedTraderTeleport;

internal sealed class TravelTransitionOverlay : MonoBehaviour
{
    private const string ObjectName = "VisitedTraderTeleportTransitionOverlay";

    private static TravelTransitionOverlay instance;

    private string message = string.Empty;
    private GUIStyle messageStyle;
    private GUIStyle hintStyle;

    public static void Show(string text)
    {
        if (GameManager.IsDedicatedServer)
        {
            return;
        }

        EnsureInstance().message = string.IsNullOrWhiteSpace(text)
            ? VTTLocalization.Get("vtt_transport_overlay")
            : text;
    }

    public static void Hide()
    {
        if (instance == null)
        {
            return;
        }

        instance.message = string.Empty;
    }

    private static TravelTransitionOverlay EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        var existing = FindObjectOfType<TravelTransitionOverlay>();
        if (existing != null)
        {
            instance = existing;
            return instance;
        }

        var gameObject = new GameObject(ObjectName);
        DontDestroyOnLoad(gameObject);
        instance = gameObject.AddComponent<TravelTransitionOverlay>();
        return instance;
    }

    private void OnGUI()
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        EnsureStyles();

        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.86f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        GUI.color = Color.white;
        float width = Mathf.Min(Screen.width * 0.82f, 900f);
        float x = (Screen.width - width) * 0.5f;
        float messageHeight = Mathf.Max(96f, messageStyle.CalcHeight(new GUIContent(message), width));
        string hint = VTTLocalization.Get("vtt_transport_overlay_hint");
        float hintHeight = Mathf.Max(36f, hintStyle.CalcHeight(new GUIContent(hint), width));
        float gap = 18f;
        float totalHeight = messageHeight + gap + hintHeight;
        float y = (Screen.height - totalHeight) * 0.5f;

        GUI.Label(new Rect(x, y, width, messageHeight), message, messageStyle);
        GUI.Label(new Rect(x, y + messageHeight + gap, width, hintHeight), hint, hintStyle);
        GUI.color = previousColor;

        Event current = Event.current;
        if (current != null &&
            (current.isMouse ||
             current.isKey ||
             current.type == EventType.ScrollWheel ||
             current.type == EventType.MouseDrag))
        {
            current.Use();
        }
    }

    private void EnsureStyles()
    {
        if (messageStyle != null && hintStyle != null)
        {
            return;
        }

        messageStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Clamp(Screen.height / 28, 24, 42),
            wordWrap = true
        };
        messageStyle.normal.textColor = Color.white;

        hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Clamp(Screen.height / 42, 16, 28),
            wordWrap = true
        };
        hintStyle.normal.textColor = new Color(0.86f, 0.86f, 0.86f, 1f);
    }
}
