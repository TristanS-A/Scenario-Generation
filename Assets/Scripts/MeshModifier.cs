using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class MeshModifier : MonoBehaviour
{
    [SerializeField] private Terrain _terrain;
    private float _multiplier = 0.05f;
    private float _minVolume = 0.01f;
    private int _resolution;
    private float[,] _noise;
    private float _density = 1;
    private float _friction = 0.05f;
    private float _depositionRate = 1f;
    private float _evapRate = 0.001f;

    // Start is called before the first frame update
    void Start()
    {
        GenerateTerrain();

        //RunAStar();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        Erode();
    }

    void GenerateTerrain()
    {
       /* _resolution = _terrain.terrainData.heightmapResolution;
        _noise = new float[_resolution, _resolution];

        for (int x = 0; x < _resolution; x++)
        {
            for (int y = 0; y < _resolution; y++)
            {
                _noise[x, y] = Mathf.PerlinNoise(x * _multiplier * _multiplier, y * _multiplier * 0.5f) * _multiplier;
            }
        }

        _terrain.terrainData.SetHeights(0, 0, _noise);*/

        
         _resolution = _terrain.terrainData.heightmapResolution;
        _noise = new float[_resolution, _resolution];

        for (int y = 0; y < _resolution; y++)
        {
            for (int x = 0; x < _resolution; x++)
            {
                float value = 0;
                for (int octive = 1; octive <= 4; octive*= 2)
                {
                    float scale = octive / (_resolution / 2f);
                    value += (1f / octive) * Mathf.PerlinNoise(x * scale, y * scale);
                }

                Vector2 center =  new Vector2(_resolution / 2, _resolution / 2);
                Vector2 pos = new Vector2(x, y);
                //value -= (center - pos).magnitude /(4* _resolution);
                _noise[x, y] = value / 2;
            }
        }

        _terrain.terrainData.SetHeights(0, 0, _noise);
         
    }

    void RunAStar()
    {
        AStar aStar = new AStar();
        List<Vector2Int> path = aStar.generatePath(_noise, new Vector2Int(_resolution - 1, _resolution - 1), new Vector2Int(0, 0));
        Debug.Log(path.Count);

        Vector3 scale = _terrain.terrainData.heightmapScale;
        Debug.Log(scale);

        for (int i = 0; i < path.Count - 1; i++)
        {
            GameObject empty = new GameObject();
            LineRenderer line = empty.AddComponent<LineRenderer>();

            Vector3 pos1 = new Vector3(path[i].x * scale.x, _noise[path[i].x, path[i].y] * scale.y, path[i].y * scale.z);
            Vector3 pos2 = new Vector3(path[i + 1].x * scale.x, _noise[path[i + 1].x, path[i + 1].y] * scale.y, path[i + 1].y * scale.z);

            line.enabled = true;
            empty.transform.position = pos1;
            line.SetPosition(0, pos1);
            line.SetPosition(1, pos2);
        }
    }

    void Erode()
    {
        Drop drop = new Drop(new Vector2(UnityEngine.Random.Range(1, _resolution - 1), UnityEngine.Random.Range(1, _resolution - 1)));

        while (drop.volume > _minVolume)
        {
            Vector2Int dropPos = new Vector2Int((int)drop.pos.x, (int)drop.pos.y);
            if (dropPos.x <= 0 || dropPos.y <= 0 || dropPos.x >= _resolution - 1 || dropPos.y >= _resolution - 1) break;

            Vector2Int gradient = CalculateGradient(_noise, dropPos);

            // Accelerate particle using newtonian mechanics using the surface normal.
            drop.speed = new Vector2(gradient.x, gradient.y);  // F = ma, so a = F/m
            drop.pos += drop.speed;
            drop.speed *= (1.0f - _friction);  // Friction Factor

            if ((int)drop.pos.x < 0 || (int)drop.pos.y < 0 || (int)drop.pos.x >= _resolution || (int)drop.pos.y >= _resolution) break;

            float deltaHeight = (_noise[dropPos.x, dropPos.y] - _noise[(int)drop.pos.x, (int)drop.pos.y]);

            // Compute sediment capacity difference
            float maxsediment = drop.volume * drop.speed.magnitude * drop.pickup; //Uses delta height of old vs new height

            //Stops from using negative sediment and going uphill
            if (maxsediment < 0.0f || deltaHeight < 0)
            {
                maxsediment = 0.0f;
            }

            float sdiff = Mathf.Max(0, maxsediment);

            // Act on the heights and Droplet!
            drop.sediment += _depositionRate * sdiff;
            _noise[dropPos.x, dropPos.y] -= (drop.volume * _depositionRate * sdiff);

            // Evaporate the Droplet (Note: Proportional to Volume! Better: Use shape factor to make proportional to the area instead.)
            drop.volume *= (1.0f - _evapRate);
        }

        _terrain.terrainData.SetHeights(0, 0, _noise);
    }

    Vector2Int CalculateGradient(float[,] noise, Vector2Int pos)
    {
        Dictionary<float, Vector2Int> map = new Dictionary<float, Vector2Int>();

        float top = noise[pos.x, pos.y - 1];
        float bottom = noise[pos.x, pos.y + 1];
        float left = noise[pos.x - 1, pos.y];
        float right = noise[pos.x + 1, pos.y];

        map[top] = Vector2Int.up;
        map[bottom] = Vector2Int.down;
        map[left] = Vector2Int.left;
        map[right] = Vector2Int.right;

        float min = Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom));

        return map[min];
    }
}
