using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;
using static UnityEditor.PlayerSettings;

public class MeshModifier : MonoBehaviour
{
    [SerializeField] private Terrain _terrain;
    [SerializeField] private GameObject _spline;
    [SerializeField] private MeshFilter _currRoadMeshFilter;
    [SerializeField] private GameObject _car;
    private float _minVolume = 0.01f;
    private int _resolution;
    private float[,] _noise;
    private float _friction = 0.05f;
    private float _depositionRate = 1f;
    private float _evapRate = 0.001f;
    private bool _runningErosion = false;
    private int _gridMaskSize = 5;
    private float _maxAStarSlope = 1f;

    private List<Vector3> _splineVertsP1 = new List<Vector3>();
    private List<Vector3> _splineVertsP2 = new List<Vector3>();
    private int _splineResolution = 1000;
    private float _roadWidth = 1.5f;
    private SplineContainer _currSplineContainer;
    private GameObject _currCar;
    private bool _carIsDriving = false;
    private float _carSpeedDampener = 0.01f;
    private float _carSpeed = 1;
    private float _roadOffsetY = 1;
    private bool _keepCamBehindCar = true;

    // Start is called before the first frame update
    void Start()
    {
        GenerateTerrain();
        applyTextures();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (_runningErosion)
        {
            Erode();
        }

        if (_carIsDriving)
        {
            driveCar();
        }
    }

    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 175, 240), "Test Menu");

        GUI.Label(new Rect(175 / 2 - 165 / 2 + 10, 35, 165, 20), "Anisotropic A* Grid Size: " + _gridMaskSize.ToString());
        _gridMaskSize = (int)GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 60, 80, 20), _gridMaskSize, 1, 20);

        GUI.Label(new Rect(175 / 2 - 115 / 2 + 10, 35 + 40, 115, 20), "A* Max Slope: " + _maxAStarSlope.ToString("0.00"));
        _maxAStarSlope = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 60 + 40, 80, 20), _maxAStarSlope, 0, 2);

        if (GUI.Button(new Rect(175 / 2 - 40 + 10, 120, 80, 20), "Run A*"))
        {
            RunAStar();
            _currCar = Instantiate(_car);
            _carIsDriving = true;
        }

        if (GUI.Button(new Rect(175 / 2 - 50 + 10, 145, 100, 20), _runningErosion ? "Stop Erosion" : "Start Erosion"))
        {
            _runningErosion = !_runningErosion;
        }

        if (GUI.Button(new Rect(175 / 2 - 60 + 10, 175, 120, 20), "Re-Apply Textures"))
        {
            applyTextures();
        }

        GUI.Label(new Rect(175 / 2 - 115 / 2 + 15, 205, 115, 20), "Car Speed: " + _carSpeed.ToString("0.00"));
        _carSpeed = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 225, 80, 20), _carSpeed, 0, 20);
    }
    void GenerateTerrain()
    {
        TerrainData terrainData = _terrain.terrainData;
         _resolution = terrainData.heightmapResolution;
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

        terrainData.SetHeights(0, 0, _noise);        
    }

    private void applyTextures()
    {
        TerrainData terrainData = _terrain.terrainData;
        float[,,] splatmapData = new float[terrainData.alphamapWidth, terrainData.alphamapHeight, terrainData.alphamapLayers];

        for (int y = 0; y < terrainData.alphamapHeight; y++)
        {
            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {

                // read the height at this location
                float height = _noise[x, y];

                // determine the mix of textures 1, 2 & 3 to use 
                // (using a vector3, since it can be lerped & normalized)

                float neighborHeightDiff = 0;
                if (x > 0 && y > 0 && x < _resolution - 1 && y < _resolution - 1)
                {
                    neighborHeightDiff = getNeighborHeightChange(new Vector2Int(x, y));
                }

                Vector3 splat = new Vector3(0, 1, 0);

                //Or use: if (Vector3.Dot(_terrain.terrainData.GetInterpolatedNormal((float)y / _resolution, (float)x / _resolution), Vector3.up) < 0.9f)
                if (height > 0.12f)
                {
                    splat = new Vector3(0, 0, 1);
                }
                else
                {
                    if (neighborHeightDiff > 2f)
                    {
                        splat = new Vector3(0, 1, 0);
                    }
                    else
                    {
                        splat = new Vector3(1, 0, 0);
                    }
                }

                // now assign the values to the correct location in the array
                splat.Normalize();
                splatmapData[x, y, 0] = splat.x;
                splatmapData[x, y, 1] = splat.y;
                splatmapData[x, y, 2] = splat.z;
            }
        }
        terrainData.SetAlphamaps(0, 0, splatmapData);
    }

    void RunAStar()
    {
        AStar aStar = new AStar();
        List<Vector2Int> path = aStar.generatePath(_noise, new Vector2Int(_resolution - 1, _resolution - 1), new Vector2Int(0, 0), _gridMaskSize, _maxAStarSlope);
        Debug.Log(path.Count);

        Vector3 scale = _terrain.terrainData.heightmapScale;

        SplineContainer[] splineContainers = FindObjectsOfType<SplineContainer>();
        foreach (SplineContainer sc in splineContainers)
        {
            Destroy(sc.gameObject);
        }

        SplineContainer spline = Instantiate(_spline).GetComponent<SplineContainer>();
        _currSplineContainer = spline;

        float totalLength = 0;

        for (int i = 0; i < path.Count; i++)
        {
            spline.Spline.Add(new BezierKnot(new Vector3(path[i].x * scale.x, _noise[path[i].y, path[i].x] * scale.y, path[i].y * scale.z)), 0);
        }

        /*LineRenderer[] lineRenderers = FindObjectsOfType<LineRenderer>();
        foreach (LineRenderer lr in lineRenderers)
        {
            Destroy(lr.gameObject);
        }

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
        }*/

        Debug.Log("Total Path Length: " + totalLength);

        GetSplineVerts();
        GenerateRoadMesh();
    }

    void Erode()
    {
        Drop drop = new Drop(new Vector2(UnityEngine.Random.Range(1, _resolution - 1), UnityEngine.Random.Range(1, _resolution - 1)));

        while (drop.volume > _minVolume)
        {
            Vector2Int dropPos = new Vector2Int((int)Mathf.Floor(drop.pos.x), (int)Mathf.Floor(drop.pos.y));
            if (dropPos.x <= 0 || dropPos.y <= 0 || dropPos.x >= _resolution - 1 || dropPos.y >= _resolution - 1) break;

            Vector2Int gradient = CalculateGradient(_noise, dropPos, drop.speed.normalized);

            // Accelerate particle using newtonian mechanics using the surface normal.
            drop.speed += new Vector2(gradient.x, gradient.y).normalized;  // F = ma, so a = F/m
            drop.pos += drop.speed.normalized;
            drop.speed *= (1.0f - _friction);  // Friction Factor

            if ((int)Mathf.Floor(drop.pos.x) < 0 || (int)Mathf.Floor(drop.pos.y) < 0 || (int)Mathf.Floor(drop.pos.x) >= _resolution || (int)Mathf.Floor(drop.pos.y) >= _resolution) break;

            float deltaHeight = (_noise[dropPos.x, dropPos.y] - _noise[(int)Mathf.Floor(drop.pos.x), (int)Mathf.Floor(drop.pos.y)]);

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

            /////////Calcuate avarage height from neighbors to smooth erosion
            /////////Make less erosion depending on how full the sediment level is

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

    private float getNeighborHeightChange(Vector2Int point)
    {
        Vector3Int off = new Vector3Int(1, 1, 0);
        float hC = _terrain.terrainData.GetHeight(point.y, point.x);
        float hL = _terrain.terrainData.GetHeight(point.y, point.x - off.x);
        float hR = _terrain.terrainData.GetHeight(point.y, point.x + off.x);
        float hD = _terrain.terrainData.GetHeight(point.y - off.y, point.x);
        float hU = _terrain.terrainData.GetHeight(point.y + off.y, point.x);

        float totalHightChange = Mathf.Abs(hL - hC) + Mathf.Abs(hR - hC) + Mathf.Abs(hD - hC) + Mathf.Abs(hU - hC);

        return totalHightChange;
    }

    private void GetRoadWidthSegment(float t, out Vector3 pos1, out Vector3 pos2)
    {
        Unity.Mathematics.float3 position;
        Unity.Mathematics.float3 forward;
        Unity.Mathematics.float3 up;
        _currSplineContainer.Evaluate(t, out position, out forward, out up);

         Unity.Mathematics.float3 right = Vector3.Cross(forward, up).normalized;
        pos1 = position + _roadWidth * right;
        pos2 = position - _roadWidth * right;
    }

    private void GetSplineVerts()
    {
        _splineVertsP1 = new();
        _splineVertsP2 = new();

        float step = 1f / (float)_splineResolution;
        for (int i = 0; i < _splineResolution; i++)
        {
            float time = step * i;
            GetRoadWidthSegment(time, out Vector3 pos1, out Vector3 pos2);
            _splineVertsP1.Add(pos1);
            _splineVertsP2.Add(pos2);
        }
    }

    private void GenerateRoadMesh()
    {
        Mesh roadMesh = new Mesh();
        List<Vector3> roadVerts = new List<Vector3>();
        List<int> roadTris = new List<int>();
        List<Vector2> roadUVs = new List<Vector2>();

        int offset = 0;
        float uvOffset = 0;
        int length = _splineVertsP2.Count;

        for (int i = 1; i <= length; i++)
        {
            Vector3 p1 = _splineVertsP1[i - 1];
            Vector3 p2 = _splineVertsP2[i - 1];
            Vector3 p3;
            Vector3 p4;

            if (i == length)
            {
                p3 = _splineVertsP1[0];
                p4 = _splineVertsP2[0];
            }
            else
            {
                p3 = _splineVertsP1[i];
                p4 = _splineVertsP2[i];
            }

            offset = 4 * (i - 1);

            int t1 = offset;
            int t2 = offset + 2;
            int t3 = offset + 3;

            int t4 = t3;
            int t5 = offset + 1;
            int t6 = t1;

            roadVerts.AddRange(new List<Vector3> { p1, p2, p3, p4 });
            roadTris.AddRange(new List<int> { t1, t2, t3, t4, t5, t6 });

            float distance = Vector3.Distance(p1, p3) / 4f;
            float uvDistance = uvOffset + distance;
            roadUVs.AddRange(new List<Vector2> { new Vector2(uvOffset, 0), new Vector2(uvOffset, 1), new Vector2(uvDistance, 0), new Vector2(uvDistance, 1) });
            uvOffset += distance;
        }

        roadMesh.SetVertices(roadVerts);
        roadMesh.SetTriangles(roadTris, 0);
        roadMesh.SetUVs(0, roadUVs);
        roadMesh.name = "Road Mesh";
        _currRoadMeshFilter.mesh = roadMesh;
        _currRoadMeshFilter.transform.position = new Vector3(0, _roadOffsetY, 0);
    }

    private void driveCar()
    {
        Unity.Mathematics.float3 position;
        Unity.Mathematics.float3 forward;
        Unity.Mathematics.float3 up;
        float time = Time.time * _carSpeedDampener * _carSpeed;
        _currSplineContainer.Evaluate(time - (int)time, out position, out forward, out up);

        Unity.Mathematics.float3 carOffset = new Vector3(0.5f, _roadOffsetY + _currCar.transform.lossyScale.y * 0.5f, 0);

        if (_currCar != null)
        {
            _currCar.transform.position = position + carOffset;
            _currCar.transform.LookAt(_currCar.transform.position + (Vector3)forward);
        }

        Vector3 camLookOffset = new Vector3(Mathf.Cos(Time.time * 0.3f), Mathf.Cos(Time.time * 0.2f) * 0.2f, Mathf.Sin(Time.time * 0.3f));
        Vector3 camPos = _currCar.transform.position + (Vector3)up * 0.5f + camLookOffset;

        if (_keepCamBehindCar)
        {
            camPos -= ((Vector3)forward).normalized * 2f;
        }

        Vector3 camDirToCar = (_currCar.transform.position - camPos).normalized;
        Camera.main.transform.position = camPos - camDirToCar * 4 + new Vector3(0, Mathf.Abs(Vector3.Dot(up, Vector3.right)), 0) * 4;
        Camera.main.transform.LookAt(_currCar.transform.position);
    }
}
