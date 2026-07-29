using UnityEngine;

public class MC_Rotate : MonoBehaviour
{
    [Header("Простое вращение (когда Randomize = ВЫКЛ)")]
    public float x;
    public float y;
    public float z;

    [Header("Случайное вращение")]
    public bool Randomize = false;

    [Tooltip("Если включено — вращение только по оси Y (в режиме Randomize).")]
    public bool randomizeOnlyY = true;

    [Tooltip("Диапазон случайной скорости вращения (градусы в секунду).")]
    public Vector2 randomSpeedRange = new Vector2(10f, 60f);

    [Tooltip("Сколько времени объект будет крутиться в одном направлении (секунды).")]
    public Vector2 rotateTimeRange = new Vector2(0.5f, 2.5f);

    [Tooltip("Диапазон паузы между вращениями (секунды).")]
    public Vector2 pauseTimeRange = new Vector2(0.0f, 1.0f);

    [Tooltip("Если включено — между вращениями будут паузы. Если выключено — вращение без остановок.")]
    public bool usePauses = true;

    [Tooltip("Если включено — направление вращения будет случайно меняться (вправо / влево).")]
    public bool randomizeDirection = true;

    [Tooltip("Применять вращение в мировом пространстве или локальном.")]
    public Space space = Space.Self;

    private float _currentSpeedDegPerSec;
    private float _segmentTimer;
    private float _pauseTimer;
    private bool _isPaused;
    private int _dirSign = 1;

    private void OnEnable()
    {
        // Сброс состояния случайного вращения при включении объекта
        ResetRandomState();
    }

    private void ResetRandomState()
    {
        _isPaused = false;
        _pauseTimer = 0f;
        StartNewRotateSegment();
    }

    private void StartNewRotateSegment()
    {
        // Приводим диапазон скорости в корректный вид
        float minSpeed = Mathf.Min(randomSpeedRange.x, randomSpeedRange.y);
        float maxSpeed = Mathf.Max(randomSpeedRange.x, randomSpeedRange.y);
        _currentSpeedDegPerSec = Random.Range(minSpeed, maxSpeed);

        // Задаём случайную длительность вращения
        float minRotT = Mathf.Min(rotateTimeRange.x, rotateTimeRange.y);
        float maxRotT = Mathf.Max(rotateTimeRange.x, rotateTimeRange.y);
        _segmentTimer = Random.Range(minRotT, maxRotT);

        // Определяем направление вращения
        if (randomizeDirection)
            _dirSign = (Random.value < 0.5f) ? -1 : 1;
        else
            _dirSign = 1;
    }

    private void StartPause()
    {
        // Задаём случайную паузу между вращениями
        float minPause = Mathf.Min(pauseTimeRange.x, pauseTimeRange.y);
        float maxPause = Mathf.Max(pauseTimeRange.x, pauseTimeRange.y);
        _pauseTimer = Random.Range(minPause, maxPause);
        _isPaused = _pauseTimer > 0f;
    }

    void Update()
    {
        if (!Randomize)
        {
            // Обычное вращение (как в самом простом скрипте)
            transform.Rotate(x, y, z, space);
            return;
        }

        // Режим случайного вращения: сегменты + паузы
        if (_isPaused)
        {
            _pauseTimer -= Time.deltaTime;
            if (_pauseTimer <= 0f)
            {
                _isPaused = false;
                StartNewRotateSegment();
            }
            return;
        }

        // Вращаем объект в текущем кадре
        float deltaDeg = _currentSpeedDegPerSec * _dirSign * Time.deltaTime;

        if (randomizeOnlyY)
        {
            transform.Rotate(0f, deltaDeg, 0f, space);
        }
        else
        {
            // Если понадобится — вращение по всем осям
            transform.Rotate(x * deltaDeg, y * deltaDeg, z * deltaDeg, space);
        }

        // Отсчёт времени текущего сегмента вращения
        _segmentTimer -= Time.deltaTime;
        if (_segmentTimer <= 0f)
        {
            if (usePauses)
                StartPause();
            else
                StartNewRotateSegment();
        }
    }
}
