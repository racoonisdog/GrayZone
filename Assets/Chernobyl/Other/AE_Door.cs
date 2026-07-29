using UnityEngine;

public class AE_Door : MonoBehaviour
{
    bool trig, open; // trig - проверка входа/выхода в триггер (игрок должен быть с тегом Player), open - закрыть и открыть дверь
    public float smooth = 2.0f; // скорость вращения
    public float DoorOpenAngle = 87.0f; // угол вращения
    private Quaternion defaultRot;
    private Quaternion openRot;

    private bool isKeyPressed; // Флаг для отслеживания состояния нажатия клавиши

    [Header("GUI Settings")]
    public string openMessage = "Open E"; // Сообщение при закрытой двери
    public string closeMessage = "Close E"; // Сообщение при открытой двери
    public Font messageFont; // Шрифт
    public int fontSize = 24; // Размер шрифта
    public Color fontColor = Color.white; // Цвет шрифта
    public Vector2 messagePosition = new Vector2(0.5f, 0.5f); // Позиция на экране (относительные координаты от 0 до 1)

    private string doorMessage = ""; // Сообщение для отображения

    private void Start()
    {
        defaultRot = transform.rotation;
        openRot = Quaternion.Euler(transform.eulerAngles.x, transform.eulerAngles.y + DoorOpenAngle, transform.eulerAngles.z);
        isKeyPressed = false; // Инициализация флага
    }

    private void Update()
    {
        if (open) // открыть
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, openRot, Time.deltaTime * smooth);
        }
        else // закрыть
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, defaultRot, Time.deltaTime * smooth);
        }

        if (Input.GetKeyDown(KeyCode.E) && trig && !isKeyPressed)
        {
            open = !open;
            isKeyPressed = true; // Установка флага в true при нажатии клавиши
        }

        if (Input.GetKeyUp(KeyCode.E))
        {
            isKeyPressed = false; // Сброс флага при отпускании клавиши
        }

        if (trig)
        {
            doorMessage = open ? closeMessage : openMessage;
        }
        else
        {
            doorMessage = "";
        }
    }

    private void OnGUI()
    {
        if (!string.IsNullOrEmpty(doorMessage))
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = fontSize;
            style.normal.textColor = fontColor;
            if (messageFont != null)
            {
                style.font = messageFont;
            }

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;
            Vector2 labelSize = style.CalcSize(new GUIContent(doorMessage));

            // Используем относительные координаты для позиции на экране
            float labelX = screenWidth * messagePosition.x - labelSize.x / 2;
            float labelY = screenHeight * messagePosition.y - labelSize.y / 2;

            GUI.Label(new Rect(labelX, labelY, labelSize.x, labelSize.y), doorMessage, style);
        }
    }

    private void OnTriggerEnter(Collider coll) // вход и выход в/из триггера
    {
        if (coll.tag == "Player")
        {
            doorMessage = open ? closeMessage : openMessage;
            trig = true;
        }
    }

    private void OnTriggerExit(Collider coll) // вход и выход в/из триггера
    {
        if (coll.tag == "Player")
        {
            doorMessage = "";
            trig = false;
        }
    }
}
