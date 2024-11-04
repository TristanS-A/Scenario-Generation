using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class MeshModifier : MonoBehaviour
{
    [SerializeField] private Terrain _terrain;
    private float _minVolume = 0.01f;
    private int _resolution;
    private float[,] _noise;
    private float _friction = 0.05f;
    private float _depositionRate = 1f;
    private float _evapRate = 0.001f;
    private bool _runningErosion = false;
    private int _gridMaskSize = 5;
    private float _maxAStarSlope = 1f;

    // Start is called before the first frame update
    void Start()
    {
        GenerateTerrain();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (_runningErosion)
        {
            Erode();
        }
    }

    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 175, 165), "Test Menu");

        GUI.Label(new Rect(175 / 2 - 165 / 2 + 10, 35, 165, 20), "Anisotropic A* Grid Size: " + _gridMaskSize.ToString());
        _gridMaskSize = (int)GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 60, 80, 20), _gridMaskSize, 1, 20);

        GUI.Label(new Rect(175 / 2 - 115 / 2 + 10, 35 + 40, 115, 20), "A* Max Slope: " + _maxAStarSlope.ToString("0.00"));
        _maxAStarSlope = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 60 + 40, 80, 20), _maxAStarSlope, 0, 2);

        if (GUI.Button(new Rect(175 / 2 - 40 + 10, 120, 80, 20), "Run A*"))
        {
            RunAStar();
        }

        if (GUI.Button(new Rect(175 / 2 - 50 + 10, 145, 100, 20), _runningErosion ? "Stop Erosion" : "Start Erosion"))
        {
            _runningErosion = !_runningErosion;
        }
    }
    void GenerateTerrain()
    {
         _resolution = _terrain.terrainData.heightmapResolution;
        _noise = new float[_resolution, _resolution];

        for (int y = 0; y < _resolution; y++)
        {
            for (int x = 0; x < _resolution; x++)
            {
                float value = 0;
                for (float octive = 2f; octive <= 4f; octive*= 2)
                {
                    float scale = octive / (_resolution / 2f);
                    value += (1f / octive) * Mathf.PerlinNoise(x * scale, y * scale);
                }

                Vector2 center =  new Vector2(_resolution / 2f, _resolution / 2f);
                Vector2 pos = new Vector2(x, y);
                value -= (center - pos).magnitude / _resolution;
                _noise[x, y] = Mathf.Max(0, value) / 2f;
            }
        }

        _terrain.terrainData.SetHeights(0, 0, _noise);
         
    }

    void RunAStar()
    {
        LineRenderer[] lineRenderers = FindObjectsOfType<LineRenderer>();
        foreach (LineRenderer lr in  lineRenderers)
        {
            Destroy(lr.gameObject);
        }

        AStar aStar = new AStar();
        List<Vector2Int> path = aStar.generatePath(_noise, new Vector2Int(_resolution - 1, _resolution - 1), new Vector2Int(0, 200), _gridMaskSize, _maxAStarSlope);
        Debug.Log(path.Count);

        Vector3 scale = _terrain.terrainData.heightmapScale;
        Debug.Log(scale);

        float totalLength = 0;
        for (int i = 0; i < path.Count - 1; i++)
        {
            GameObject empty = new GameObject();
            LineRenderer line = empty.AddComponent<LineRenderer>();

            Vector3 pos1 = new Vector3(path[i].x * scale.x, _noise[path[i].y, path[i].x] * scale.y, path[i].y * scale.z);
            Vector3 pos2 = new Vector3(path[i + 1].x * scale.x, _noise[path[i + 1].y, path[i + 1].x] * scale.y, path[i + 1].y * scale.z);

            totalLength += (pos2 - pos1).magnitude;

            line.enabled = true;
            empty.transform.position = pos1;
            line.SetPosition(0, pos1);
            line.SetPosition(1, pos2);
        }

        Debug.Log("Total Path Length: " + totalLength);
    }

    void Erode()
    {
        Drop drop = new Drop(new Vector2(UnityEngine.Random.Range(1, _resolution - 1), UnityEngine.Random.Range(1, _resolution - 1)));

        while (drop.volume > _minVolume)
        {
            Vector2Int dropPos = new Vector2Int(Mathf.RoundToInt(drop.pos.x), Mathf.RoundToInt(drop.pos.y));
            if (dropPos.x <= 0 || dropPos.y <= 0 || dropPos.x >= _resolution - 1 || dropPos.y >= _resolution - 1) break;

            Vector2Int gradient = CalculateGradient(_noise, dropPos, drop.speed.normalized);

            // Accelerate particle using newtonian mechanics using the surface normal.
            drop.speed += new Vector2(gradient.x, gradient.y).normalized;  // F = ma, so a = F/m
            drop.pos += drop.speed.normalized;
            drop.speed *= (1.0f - _friction);  // Friction Factor

            if (Mathf.RoundToInt(drop.pos.x) < 0 || Mathf.RoundToInt(drop.pos.y) < 0 || Mathf.RoundToInt(drop.pos.x) >= _resolution || Mathf.RoundToInt(drop.pos.y) >= _resolution) break;

            float deltaHeight = (_noise[dropPos.x, dropPos.y] - _noise[Mathf.RoundToInt(drop.pos.x), Mathf.RoundToInt(drop.pos.y)]);

            // Compute sediment capacity difference
            float maxsediment = drop.volume * deltaHeight; //Uses delta height of old vs new height

            //Stops from using negative sediment and going uphill
            if (maxsediment < 0.0f || deltaHeight < 0)
            {
                maxsediment = 0.0f;
            }

            float sdiff = maxsediment;

            if (drop.sediment < maxsediment)
            {
                drop.sediment += drop.volume * deltaHeight;
                drop.sediment -= _depositionRate * sdiff;
                _noise[dropPos.x, dropPos.y] -= (drop.volume * _depositionRate * sdiff);        
            }
            else if (drop.sediment >= maxsediment)
            {
                drop.sediment -= drop.volume * deltaHeight;
                drop.sediment += _depositionRate * sdiff;
                //_noise[dropPos.x, dropPos.y] -= (drop.volume * _depositionRate * sdiff);
            }

            // Evaporate the Droplet (Note: Proportional to Volume! Better: Use shape factor to make proportional to the area instead.)
            drop.volume *= (1.0f - _evapRate);
        }

        _terrain.terrainData.SetHeights(0, 0, _noise);
    }

    Vector2Int CalculateGradient(float[,] noise, Vector2Int pos, Vector2 currDirection)
    {
        Dictionary<float, Vector2Int> map = new Dictionary<float, Vector2Int>();
        List<float> neighbors = new List<float>();

        float top = noise[pos.x, pos.y - 1];
        float bottom = noise[pos.x, pos.y + 1];
        float left = noise[pos.x - 1, pos.y];
        float right = noise[pos.x + 1, pos.y];
        float topRight = noise[pos.x + 1, pos.y - 1];
        float topLeft = noise[pos.x - 1, pos.y - 1];
        float bottomRight = noise[pos.x + 1, pos.y + 1];
        float bottomLeft = noise[pos.x - 1, pos.y + 1];

        map[top] = Vector2Int.down;
        map[bottom] = Vector2Int.up;
        map[left] = Vector2Int.left;
        map[right] = Vector2Int.right;
        map[topRight] = new Vector2Int(1, -1);
        map[topLeft] = new Vector2Int(-1, -1);
        map[bottomRight] = new Vector2Int(1, 1);
        map[bottomLeft] = new Vector2Int(-1, 1);


        ////Min slope method
        //float min = Mathf.Min(Mathf.Min(Mathf.Min(topLeft, topRight), Mathf.Min(bottomLeft, bottomRight)), Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom)));
        //return map[min];


        //Calculates lowest 2 values
        neighbors.Add(top);
        neighbors.Add(bottom);
        neighbors.Add(left);
        neighbors.Add(right);
        neighbors.Add(topRight);
        neighbors.Add(topLeft);
        neighbors.Add(bottomRight);
        neighbors.Add(bottomLeft);

        float min1Value = float.MaxValue;
        float min2Value = float.MaxValue;
        for (int i = 0; i < neighbors.Count; i++)
        {
            if (neighbors[i] < min1Value)
            {
                min1Value = neighbors[i];
            }
            else if (neighbors[i] < min2Value)
            {
                min2Value = neighbors[i];
            }
        }

        Vector2Int minDirection1 = map[min1Value];
        Vector2Int minDirection2 = map[min2Value];

        ////Random of the lowest 2 directions
        //Vector2Int newDirection = UnityEngine.Random.Range(0, 2) == 1 ? minDirection1 : minDirection2;


        ////Random of the lowest 2 directions with closest direction being more likely
        float changeDir = Vector2.Dot(((Vector2)minDirection1 + (Vector2)minDirection2).normalized, currDirection);
        Vector2Int newDirection = UnityEngine.Random.Range(-1f, 1f) < changeDir ? minDirection1 : minDirection2;


        return newDirection;
    }
}
