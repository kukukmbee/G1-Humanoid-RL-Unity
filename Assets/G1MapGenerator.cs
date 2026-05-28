using UnityEngine;
using System.Collections.Generic;

public class G1MapGenerator : MonoBehaviour
{
    [Header("Generation Settings")]
    public float corridorWidth = 2.5f;     // Ширина коридора (2.5м идеально для покачивающегося робота)
    public float wallHeight = 2.0f;        // Высота стен
    public float wallThickness = 0.2f;     // Толщина стен

    [Header("Automatic Hookup")]
    public G1MissionController missionController; // Ссылка на контроллер миссии

    private List<Transform> generatedWaypoints = new List<Transform>();
    private Transform boxTransform;
    private Transform startBaseTransform;

    private void Awake()
    {
        // Находим или добавляем контроллер миссии
        if (missionController == null)
        {
            missionController = GetComponent<G1MissionController>();
            if (missionController == null)
            {
                missionController = gameObject.AddComponent<G1MissionController>();
            }
        }

        GenerateLevel();
    }

    public void GenerateLevel()
    {
        Debug.Log("[G1Generator] 🔨 Начинаем автоматическую генерацию лабиринта-коридора...");

        // Создаем пустой объект-контейнер для всей карты, чтобы не захламлять сцену
        GameObject mapContainer = new GameObject("Generated_Corridor_Map");
        mapContainer.transform.position = Vector3.zero;

        // 1. ГЕНЕРИРУЕМ НАДЕЖНЫЙ ТОЛСТЫЙ ПОЛ (30м х 30м, толщина 2м)
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Concrete_Floor";
        floor.transform.SetParent(mapContainer.transform);
        floor.transform.position = new Vector3(4f, -1.0f, 8f); // Центр плиты под лабиринтом
        floor.transform.localScale = new Vector3(30f, 2f, 30f);
        
        // Красим пол в приятный серый цвет
        Renderer floorRenderer = floor.GetComponent<Renderer>();
        if (floorRenderer != null)
        {
            floorRenderer.material.color = new Color(0.2f, 0.2f, 0.2f); // Темно-серый асфальт
        }

        // 2. СТРОИМ СТЕНЫ КОРИДОРА (S-образный лабиринт с 2 поворотами)
        // Коридор: идет прямо (по Z) -> поворачивает направо (по X) -> поворачивает налево (по Z)
        
        // --- СЕКЦИЯ 1 (Вперед по оси Z, от 0 до 8м) ---
        // Левая стена
        BuildWall("Wall_Sec1_Left", new Vector3(-corridorWidth/2f - wallThickness/2f, wallHeight/2f, 4f), new Vector3(wallThickness, wallHeight, 8f), mapContainer.transform);
        // Правая стена (чуть короче, чтобы открыть поворот направо)
        BuildWall("Wall_Sec1_Right", new Vector3(corridorWidth/2f + wallThickness/2f, wallHeight/2f, 3.375f), new Vector3(wallThickness, wallHeight, 6.75f), mapContainer.transform);

        // --- СЕКЦИЯ 2 (Поворот направо по оси X, от 0 до 8м) ---
        // Внутренний угол
        BuildWall("Wall_Sec2_Inner", new Vector3(4.0f, wallHeight/2f, corridorWidth/2f + wallThickness/2f), new Vector3(5.5f, wallHeight, wallThickness), mapContainer.transform);
        // Внешний угол
        BuildWall("Wall_Sec2_Outer", new Vector3(4.0f, wallHeight/2f, 8f + corridorWidth/2f + wallThickness/2f), new Vector3(10.5f, wallHeight, wallThickness), mapContainer.transform);

        // --- СЕКЦИЯ 3 (Поворот налево вперед по оси Z, от 8 до 15м) ---
        // Правая стена (относительно робота на этом участке)
        BuildWall("Wall_Sec3_Right", new Vector3(8f + corridorWidth/2f + wallThickness/2f, wallHeight/2f, 12.625f), new Vector3(wallThickness, wallHeight, 6.75f), mapContainer.transform);
        // Левая стена
        BuildWall("Wall_Sec3_Left", new Vector3(8f - corridorWidth/2f - wallThickness/2f, wallHeight/2f, 11.375f), new Vector3(wallThickness, wallHeight, 9.25f), mapContainer.transform);

        // 3. СОЗДАЕМ КОРОБКУ ДЛЯ ДОСТАВКИ (на финише: X=8, Z=14)
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "Delivery_Box";
        box.transform.SetParent(mapContainer.transform);
        box.transform.position = new Vector3(8f, 0.4f, 14f); // Приподнята над полом
        box.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f); // Размер посылки
        
        // Добавляем Rigidbody для физики
        Rigidbody boxRb = box.AddComponent<Rigidbody>();
        boxRb.mass = 1.0f;
        boxRb.linearDamping = 0.5f;

        // Красим коробку в яркий оранжевый/коричневый цвет посылки
        Renderer boxRenderer = box.GetComponent<Renderer>();
        if (boxRenderer != null)
        {
            boxRenderer.material.color = new Color(0.8f, 0.4f, 0.1f);
        }
        boxTransform = box.transform;

        // 4. СОЗДАЕМ ТОЧКУ СТАРТА/БАЗЫ
        GameObject startBase = new GameObject("Start_Base_Base");
        startBase.transform.SetParent(mapContainer.transform);
        startBase.transform.position = Vector3.zero;
        startBaseTransform = startBase.transform;

        // 5. СОЗДАЕМ ПУТЕВЫЕ ТОЧКИ (ЧЕКПОИНТЫ) ТОЧНО В ЦЕНТРАХ ПОВОРОТОВ КОРИДОРА
        // Чекпоинт 1: Первый поворот
        GameObject wp1 = new GameObject("Waypoint_1_Turn");
        wp1.transform.SetParent(mapContainer.transform);
        wp1.transform.position = new Vector3(0f, 0f, 8f);
        generatedWaypoints.Add(wp1.transform);

        // Чекпоинт 2: Второй поворот
        GameObject wp2 = new GameObject("Waypoint_2_Turn");
        wp2.transform.SetParent(mapContainer.transform);
        wp2.transform.position = new Vector3(8f, 0f, 8f);
        generatedWaypoints.Add(wp2.transform);

        // 6. АВТОМАТИЧЕСКИ НАСТРАИВАЕМ СВЯЗИ В MISSION CONTROLLER
        if (missionController != null)
        {
            missionController.boxObject = boxTransform;
            missionController.returnTarget = startBaseTransform;

            // Настраиваем путь ТУДА (к коробке)
            missionController.waypointsToBox.Clear();
            missionController.waypointsToBox.Add(wp1.transform);
            missionController.waypointsToBox.Add(wp2.transform);

            // Настраиваем путь ОБРАТНО (к базе)
            missionController.waypointsToHome.Clear();
            missionController.waypointsToHome.Add(wp2.transform);
            missionController.waypointsToHome.Add(wp1.transform);

            Debug.Log("[G1Generator] ✅ Все связи (коробка, база, повороты) успешно прописаны в G1MissionController!");
        }

        // 7. НАСТРАИВАЕМ ТОЧКУ ЗАКРЕПЛЕНИЯ НА РОБОТЕ (Ищем кость груди/torso у робота)
        G1Agent agent = GetComponent<G1Agent>();
        if (agent == null) agent = FindObjectOfType<G1Agent>();
        if (agent != null && missionController != null)
        {
            missionController.agent = agent;
            
            // Ищем кость груди для прикрепления коробки
            Transform[] allBones = agent.GetComponentsInChildren<Transform>();
            foreach (var bone in allBones)
            {
                string boneName = bone.name.ToLower();
                if (boneName.Contains("torso") || boneName.Contains("chest") || boneName.Contains("pelvis"))
                {
                    missionController.robotAttachPoint = bone;
                    Debug.Log($"[G1Generator] Точка крепления коробки автоматически назначена на сустав: {bone.name}");
                    break;
                }
            }
        }
    }

    private void BuildWall(string wallName, Vector3 position, Vector3 scale, Transform parent)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = wallName;
        wall.transform.SetParent(parent);
        wall.transform.position = position;
        wall.transform.localScale = scale;

        // Красим стены в красивый матовый белый/светло-серый цвет
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        if (wallRenderer != null)
        {
            wallRenderer.material.color = new Color(0.85f, 0.85f, 0.85f);
        }
    }
}
