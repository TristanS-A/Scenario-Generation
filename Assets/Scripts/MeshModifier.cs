using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    private float[,] _currNoise;
    private float[,] _nextNoise;
    private float _friction = 0.05f;
    private float _depositionRate = 1f;
    private float _evapRate = 0.001f;
    private bool _runningErosion = false;
    private int _gridMaskSize = 5;
    private float _maxAStarSlope = 1f;

    private List<Vector3> _splineVertsP1 = new List<Vector3>();
    private List<Vector3> _splineVertsP2 = new List<Vector3>();
    private int _splineResolution = 5000;
    private float _roadWidth = 1.5f;
    private SplineContainer _currSplineContainer;
    private GameObject _currCar;
    private bool _carIsDriving = false;
    private float _carSpeedDampener = 0.01f;
    private float _carSpeed = 1;
    private float _roadOffsetY = 1;
    private bool _keepCamBehindCar = true;

    void Start()
    {
        GenerateTerrain();
        applyTerrainTextures();
    }

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
        GUI.Box(new Rect(10, 10, 175, 300), "Test Menu");

        GUI.Label(new Rect(175 / 2 - 165 / 2 + 10, 35, 165, 20), "Anisotropic A* Grid Size: " + _gridMaskSize.ToString());
        _gridMaskSize = (int)GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 60, 80, 20), _gridMaskSize, 1, 20);

        GUI.Label(new Rect(175 / 2 - 115 / 2 + 10, 35 + 40, 115, 20), "A* Max Slope: " + _maxAStarSlope.ToString("0.00"));
        _maxAStarSlope = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 60 + 40, 80, 20), _maxAStarSlope, 0, 2);

        if (GUI.Button(new Rect(175 / 2 - 40 + 10, 120, 80, 20), "Run A*"))
        {
            RunAStar();

            if (_currCar != null)
            {
                Destroy(_currCar);
            }

            _currCar = Instantiate(_car);
            _carIsDriving = true;
        }

        if (GUI.Button(new Rect(175 / 2 - 50 + 10, 145, 100, 20), _runningErosion ? "Stop Erosion" : "Start Erosion"))
        {
            _runningErosion = !_runningErosion;
        }

        if (GUI.Button(new Rect(175 / 2 - 60 + 10, 175, 120, 20), "Re-Apply Textures"))
        {
            applyTerrainTextures();
        }

        GUI.Label(new Rect(175 / 2 - 115 / 2 + 15, 205, 115, 20), "Car Speed: " + _carSpeed.ToString("0.00"));
        _carSpeed = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 225, 80, 20), _carSpeed, 0, 20);

        if (GUI.Button(new Rect(175 / 2 - 75 + 10, 250, 150, 20), !_keepCamBehindCar ? "Keep Cam Behind Car" : "Have Cam Circle Car"))
        {
            _keepCamBehindCar = !_keepCamBehindCar;
        }
    }
    void GenerateTerrain()
    {
        TerrainData terrainData = _terrain.terrainData;
         _resolution = terrainData.heightmapResolution;
        _currNoise = new float[_resolution, _resolution];

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
                _currNoise[x, y] = Mathf.Max(0, value) / 2f;
            }
        }

        _nextNoise = _currNoise;
        terrainData.SetHeights(0, 0, _currNoise);        
    }

    private void applyTerrainTextures()
    {
        //Tutorial for splatmapping: https://discussions.unity.com/t/how-to-automatically-apply-different-textures-on-terrain-based-on-height/2013/2?clickref=1011lzS3tiIW&utm_source=partnerize&utm_medium=affiliate&utm_campaign=unity_affiliate
       
        TerrainData terrainData = _terrain.terrainData;
        float[,,] splatmapData = new float[terrainData.alphamapWidth, terrainData.alphamapHeight, terrainData.alphamapLayers];

        for (int y = 0; y < terrainData.alphamapHeight; y++)
        {
            for (int x = 0; x < terrainData.alphamapWidth; x++)
            {

                // read the height at this location
                float height = _currNoise[x, y];

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
        List<Vector2Int> path = aStar.generatePath(_currNoise, new Vector2Int(_resolution - 1, _resolution - 1), new Vector2Int(0, 0), _gridMaskSize, _maxAStarSlope);
        Debug.Log("Path segment count: " + path.Count);

        GeneratePointConnectorVisual(path);
        GenerateRoad(path);
    }

    void Erode()
    {
        Drop drop = new Drop(new Vector2(UnityEngine.Random.Range(1, _resolution - 1), UnityEngine.Random.Range(1, _resolution - 1)));

        while (drop.volume > _minVolume)
        {
            Vector2Int dropPos = new Vector2Int((int)Mathf.Floor(drop.pos.x), (int)Mathf.Floor(drop.pos.y));
            if (dropPos.x <= 0 || dropPos.y <= 0 || dropPos.x >= _resolution - 1 || dropPos.y >= _resolution - 1) break;

            Vector2Int newDir = CalculateNewFlowDirection(ref _currNoise, dropPos, drop.speed.normalized);

            // Accelerate particle using newtonian mechanics using the surface normal.
            drop.speed += new Vector2(newDir.x, newDir.y).normalized;  // F = ma, so a = F/m
            drop.pos += drop.speed.normalized;
            drop.speed *= (1.0f - _friction);  // Friction Factor

            if ((int)Mathf.Floor(drop.pos.x) < 0 || (int)Mathf.Floor(drop.pos.y) < 0 || (int)Mathf.Floor(drop.pos.x) >= _resolution || (int)Mathf.Floor(drop.pos.y) >= _resolution) break;

            float deltaHeight = (_currNoise[dropPos.x, dropPos.y] - _currNoise[(int)Mathf.Floor(drop.pos.x), (int)Mathf.Floor(drop.pos.y)]);

            // Compute sediment capacity difference
            float maxsediment = drop.volume * deltaHeight; //Uses delta height of old vs new height

            //Stops from using negative sediment and going uphill
            if (maxsediment < 0.0f || deltaHeight < 0)
            {
                maxsediment = 0.0f;
            }

            float sdiff = maxsediment;
            float scale = 0;

            if (drop.sediment < maxsediment)
            {
                drop.sediment += sdiff * drop.pickup;

                if (maxsediment > 0)
                {
                    scale = Mathf.Max(0, 1 - (drop.sediment / maxsediment));
                }

                _nextNoise[dropPos.x, dropPos.y] -= (scale * sdiff);
            }
            else if (drop.sediment >= maxsediment)
            {
                //_currNoise[dropPos.x, dropPos.y] += (drop.sediment * sdiff);
                //drop.sediment -= drop.sediment * sdiff;
            }


            /////////Calcuate avarage height from neighbors to smooth erosion
            /////////Make less erosion depending on how full the sediment level is

            // Evaporate the Droplet (Note: Proportional to Volume! Better: Use shape factor to make proportional to the area instead.)
            drop.volume *= (1.0f - _evapRate);
        }

        _currNoise = _nextNoise;
        _terrain.terrainData.SetHeights(0, 0, _currNoise);
    }

    Vector2Int CalculateNewFlowDirection(ref float[,] noise, Vector2Int pos, Vector2 currDirection)
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
        Vector2Int newDirection = UnityEngine.Random.Range(0, 2) == 1 ? minDirection1 : minDirection2;


        ////Random of the lowest 2 directions with closest direction being more likely
        //float changeDir = Vector2.Dot(((Vector2)minDirection1 + (Vector2)minDirection2).normalized, currDirection);
        //Vector2Int newDirection = UnityEngine.Random.Range(-1f, 1f) < changeDir ? minDirection1 : minDirection2;


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

        if (!_keepCamBehindCar)
        {
            camPos -= ((Vector3)forward).normalized * 2f;
        }

        Vector3 camDirToCar = (_currCar.transform.position - camPos).normalized;
        Camera.main.transform.position = camPos - camDirToCar * 4 + new Vector3(0, Mathf.Abs(Vector3.Dot(up, Vector3.right)), 0) * 6;
        Camera.main.transform.LookAt(_currCar.transform.position);
    }

    private void GeneratePointConnectorVisual(List<Vector2Int> path)
    {
        Vector3 scale = _terrain.terrainData.heightmapScale;

        LineRenderer[] lineRenderers = FindObjectsOfType<LineRenderer>();
        foreach (LineRenderer lr in lineRenderers)
        {
            Destroy(lr.gameObject);
        }

        float totalLength = 0;
        for (int i = 0; i < path.Count - 1; i++)
        {

            GameObject empty = new GameObject();
            LineRenderer line = empty.AddComponent<LineRenderer>();

            Vector3 pos1 = new Vector3(path[i].x * scale.x, _currNoise[path[i].y, path[i].x] * scale.y, path[i].y * scale.z);
            Vector3 pos2 = new Vector3(path[i + 1].x * scale.x, _currNoise[path[i + 1].y, path[i + 1].x] * scale.y, path[i + 1].y * scale.z);

            totalLength += (pos2 - pos1).magnitude;

            line.enabled = true;
            empty.transform.position = pos1;
            line.SetPosition(0, pos1);
            line.SetPosition(1, pos2);
        }

        Debug.Log("Total Path Length: " + totalLength);
    }

    private void GenerateRoadSpline(List<Vector2Int> path)
    {
        Vector3 scale = _terrain.terrainData.heightmapScale;

        SplineContainer[] splineContainers = FindObjectsOfType<SplineContainer>();
        foreach (SplineContainer sc in splineContainers)
        {
            Destroy(sc.gameObject);
        }

        SplineContainer spline = Instantiate(_spline).GetComponent<SplineContainer>();
        _currSplineContainer = spline;

        for (int i = 0; i < path.Count; i++)
        {
            spline.Spline.Add(new BezierKnot(new Vector3(path[i].x * scale.x, _currNoise[path[i].y, path[i].x] * scale.y, path[i].y * scale.z)), 0);
        }

        spline.GetComponent<MeshRenderer>().enabled = false;
    }

    public void GenerateRoad(List<Vector2Int> path)
    {
        GenerateRoadSpline(path);
        RoadGenerator.GetSplineVerts(_currSplineContainer, _roadWidth, _splineResolution, ref _splineVertsP1, ref _splineVertsP2);
        _currRoadMeshFilter.mesh = RoadGenerator.GenerateRoadMesh(_splineVertsP1, _splineVertsP2);
        _currRoadMeshFilter.transform.position = new Vector3(0, _roadOffsetY, 0);
    }
}
