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
    [SerializeField] private GameObject _startingLoc;
    [SerializeField] private GameObject _endLoc;
    private float _minVolume = 0.01f;
    private int _resolution;
    private float[,] _currNoise;
    private float _friction = 0.05f;
    private float _evapRate = 0.001f;
    private float _smoothAmount = 0.5f;
    private bool _runningErosion = false;
    private int _gridMaskSize = 5;
    private int _minSegmentSize = 1;
    private float _maxAStarSlope = 1f;

    private int _splineResolution = 5000;
    private float _roadWidth = 1.5f;
    private SplineContainer _currSplineContainer;
    private GameObject _currCar;
    private bool _carIsDriving = false;
    private float _carSpeedDampener = 0.0005f;
    private float _carSpeed = 1;
    private float _roadOffsetY = 1;
    private bool _keepCamBehindCar = true;
    private float _carTime = 0;

    void Start()
    {
        GenerateTerrain();
        applyTerrainTextures();
    }

    void FixedUpdate()
    {
        if (_runningErosion) //Erodes while true
        {
            Erode();
        }

        if (_carIsDriving) //Drives car while true
        {
            driveCar();
        }
    }

    void OnGUI() //GUI stuff
    {
        GUI.Box(new Rect(10, 10, 175, 340), "Test Menu");

        GUI.Label(new Rect(175 / 2 - 165 / 2 + 10, 35, 165, 20), "Anisotropic A* Grid Size: " + _gridMaskSize.ToString());
        _gridMaskSize = (int)GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 60, 80, 20), _gridMaskSize, 1, 20);

        if (_gridMaskSize < _minSegmentSize)
        {
            _minSegmentSize = _gridMaskSize;
        }

        GUI.Label(new Rect(175 / 2 - 145 / 2 + 10, 35 + 40, 155, 20), "Min A* Segment Size: " + _minSegmentSize.ToString());
        _minSegmentSize = (int)GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 100, 80, 20), _minSegmentSize, 1, _gridMaskSize);

        GUI.Label(new Rect(175 / 2 - 115 / 2 + 10, 115, 120, 20), "A* Max Slope: " + _maxAStarSlope.ToString("0.00"));
        _maxAStarSlope = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 135, 80, 20), _maxAStarSlope, 0, 2);

        if (GUI.Button(new Rect(175 / 2 - 40 + 10, 152, 80, 20), "Run A*"))
        {
            RunAStar();

            if (_currCar != null)
            {
                Destroy(_currCar);
            }

            _currCar = Instantiate(_car);
            _carIsDriving = true;
        }

        if (GUI.Button(new Rect(175 / 2 - 60 + 10, 180, 120, 20), "Re-Apply Textures"))
        {
            applyTerrainTextures();
        }

        GUI.Label(new Rect(175 / 2 - 115 / 2 + 15, 205, 115, 20), "Car Speed: " + _carSpeed.ToString("0.00"));
        _carSpeed = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 230, 80, 20), _carSpeed, 0, 20);

        if (GUI.Button(new Rect(175 / 2 - 75 + 10, 250, 150, 20), !_keepCamBehindCar ? "Keep Cam Behind Car" : "Have Cam Circle Car"))
        {
            _keepCamBehindCar = !_keepCamBehindCar;
        }

        if (GUI.Button(new Rect(175 / 2 - 50 + 10, 285, 100, 20), _runningErosion ? "Stop Erosion" : "Start Erosion"))
        {
            _runningErosion = !_runningErosion;
        }

        GUI.Label(new Rect(175 / 2 - 150 / 2 + 15, 310, 150, 20), "Erosion Smoothing: " + _smoothAmount.ToString("0.00"));
        _smoothAmount = GUI.HorizontalSlider(new Rect(175 / 2 - 40 + 10, 330, 80, 20), _smoothAmount, 0, 1);
    }
    void GenerateTerrain()
    {
        TerrainData terrainData = _terrain.terrainData;
         _resolution = terrainData.heightmapResolution;
        _currNoise = new float[_resolution, _resolution];

        //Creates noise with octives
        for (int y = 0; y < _resolution; y++)
        {
            for (int x = 0; x < _resolution; x++)
            {
                float value = 0;
                for (float octive = 1f; octive <= 4f; octive*= 2)
                {
                    float scale = octive / (_resolution / 2f);
                    value += (1f / octive) * Mathf.PerlinNoise(x * scale, y * scale);
                }

                //Island formation
                Vector2 center =  new Vector2(_resolution / 2f, _resolution / 2f);
                Vector2 pos = new Vector2(x, y);
                value -= (center - pos).magnitude / _resolution;
                _currNoise[x, y] = Mathf.Max(0, value) / 2f;
            }
        }

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
                //Gets current height
                float height = _currNoise[x, y];

                float neighborHeightDiff = 0;
                if (x > 0 && y > 0 && x < _resolution - 1 && y < _resolution - 1)
                {
                    //Calculates the height difference of the neighbors (The value will be higher if on a slope)
                    //Or use: if (Vector3.Dot(_terrain.terrainData.GetInterpolatedNormal((float)y / _resolution, (float)x / _resolution), Vector3.up) < 0.9f) to tell if is a slope
                    neighborHeightDiff = getNeighborHeightChange(new Vector2Int(x, y)); 
                }

                Vector3 splat = new Vector3(1, 0, 0);

                if (height > 0.3f) //Sets to snow if height is greater than snow level
                {
                    splat = new Vector3(0, 0, 1);
                }
                else
                {
                    if (neighborHeightDiff > 2f) //Sets to rock if heightDifference ("slope") is greater than heightDifference value
                    {
                        splat = new Vector3(0, 1, 0);
                    }
                    else //Otherwise sets to grass if flatter than heightDifference cap
                    {
                        splat = new Vector3(1, 0, 0);
                    }
                }

                //Updates spaltmapData
                splat.Normalize();
                splatmapData[x, y, 0] = splat.x;
                splatmapData[x, y, 1] = splat.y;
                splatmapData[x, y, 2] = splat.z;
            }
        }

        terrainData.SetAlphamaps(0, 0, splatmapData);
    }

    void RunAStar() //Runs A* search
    {
        AStar aStar = new AStar();

        //Makes sure start and end points are in the grid range
        _startingLoc.transform.position = new Vector3(Mathf.Clamp(_startingLoc.transform.position.x, 0, _resolution - 1), _startingLoc.transform.position.y, Mathf.Clamp(_startingLoc.transform.position.z, 0, _resolution - 1));
        _endLoc.transform.position = new Vector3(Mathf.Clamp(_endLoc.transform.position.x, 0, _resolution - 1), _endLoc.transform.position.y, Mathf.Clamp(_endLoc.transform.position.z, 0, _resolution - 1));

        //Sets start and end values
        Vector2Int start = new Vector2Int((int)_startingLoc.transform.position.x, (int)_startingLoc.transform.position.z);
        Vector2Int end = new Vector2Int((int)_endLoc.transform.position.x, (int)_endLoc.transform.position.z);

        //Generates path
        List<Vector2Int> path = aStar.generatePath(_currNoise, start, end, _gridMaskSize, _minSegmentSize, _maxAStarSlope);
        Debug.Log("Path segment count: " + path.Count);

        //Generates path visual and car road
        GeneratePointConnectorVisual(path);
        GenerateRoad(path);
    }

    void Erode()
    {
        Drop drop = new Drop(new Vector2(UnityEngine.Random.Range(1, _resolution - 1), UnityEngine.Random.Range(1, _resolution - 1)));

        //While drop still has enough volume
        while (drop.volume > _minVolume)
        {
            Vector2Int dropPos = new Vector2Int((int)Mathf.Floor(drop.pos.x), (int)Mathf.Floor(drop.pos.y));
            //Exits if drop is off grid
            if (dropPos.x <= 0 || dropPos.y <= 0 || dropPos.x >= _resolution - 1 || dropPos.y >= _resolution - 1) break;

            //Finds the new direction for the drop to move
            Vector2Int newDir = CalculateNewFlowDirection(ref _currNoise, dropPos, drop.speed.normalized);

            // Accelerate particle using new direction to move
            drop.speed += new Vector2(newDir.x, newDir.y).normalized;
            drop.pos += drop.speed.normalized * 0.5f; //Multiplies by 0.5 to stop "jumping" over corners
            drop.speed *= (1.0f - _friction);  // Friction Factor

            //Exits if drop falls off grid
            if ((int)Mathf.Floor(drop.pos.x) < 0 || (int)Mathf.Floor(drop.pos.y) < 0 || (int)Mathf.Floor(drop.pos.x) >= _resolution || (int)Mathf.Floor(drop.pos.y) >= _resolution) break;

            //Calculates height dif
            float deltaHeight = (_currNoise[dropPos.x, dropPos.y] - _currNoise[(int)Mathf.Floor(drop.pos.x), (int)Mathf.Floor(drop.pos.y)]);

            // Compute sediment capacity height difference
            float maxsediment = drop.volume * deltaHeight; //Uses delta height of old vs new height

            //Stops from using negative sediment and going uphill
            if (maxsediment < 0.0f || deltaHeight < 0)
            {
                maxsediment = 0.0f;
            }

            float sdiff = maxsediment;
            float scale = 0;

            if (drop.sediment < maxsediment) //While the sediment is below the capacity, adds sediment and erodes
            {
                drop.sediment += sdiff * drop.pickup; //Adds sediment based on pickup value

                //Scales erosion to erode based on how much sediment the drop has
                if (maxsediment > 0)
                {
                    scale = Mathf.Max(0, (1 - (drop.sediment / maxsediment)));
                }

                float newHeight = _currNoise[dropPos.x, dropPos.y] - (scale * sdiff); //Calculates new eroded height
                float smoothedHeight = calculateAvarageHeight(dropPos.x, dropPos.y); //Calculates the smoothed version of the height

                //Applies new height to map based on amount of smoothness
                _currNoise[dropPos.x, dropPos.y] = Mathf.Lerp(newHeight, smoothedHeight, _smoothAmount);

            }
            else if (drop.sediment >= maxsediment) //If sediment is above/equal to capacity, it drops sediment
            {
                drop.sediment -= drop.sediment * sdiff; //Drops sediment
                _currNoise[dropPos.x, dropPos.y] += (drop.sediment * sdiff); //Adds to height map
            }

            // Evaporate the drop
            drop.volume *= (1.0f - _evapRate);
        }

        //Updates heightmap
        _terrain.terrainData.SetHeights(0, 0, _currNoise);
    }

    Vector2Int CalculateNewFlowDirection(ref float[,] noise, Vector2Int pos, Vector2 currDirection)
    {
        //Uses map to connect neighbor heights to the direction it is in (to avoid a bunch of if statments)
        Dictionary<float, Vector2Int> map = new Dictionary<float, Vector2Int>();
        List<float> neighbors = new List<float>();

        //Gets heights for neighbors
        float top = noise[pos.x, pos.y - 1];
        float bottom = noise[pos.x, pos.y + 1];
        float left = noise[pos.x - 1, pos.y];
        float right = noise[pos.x + 1, pos.y];
        float topRight = noise[pos.x + 1, pos.y - 1];
        float topLeft = noise[pos.x - 1, pos.y - 1];
        float bottomRight = noise[pos.x + 1, pos.y + 1];
        float bottomLeft = noise[pos.x - 1, pos.y + 1];

        //Links heights to directions
        map[top] = Vector2Int.down;
        map[bottom] = Vector2Int.up;
        map[left] = Vector2Int.left;
        map[right] = Vector2Int.right;
        map[topRight] = new Vector2Int(1, -1);
        map[topLeft] = new Vector2Int(-1, -1);
        map[bottomRight] = new Vector2Int(1, 1);
        map[bottomLeft] = new Vector2Int(-1, 1);


        ////Min slope method : Gets the lowest neighbor and returns that direction
        //float min = Mathf.Min(Mathf.Min(Mathf.Min(topLeft, topRight), Mathf.Min(bottomLeft, bottomRight)), Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom)));
        //return map[min];


        ////Calculates lowest 2 values
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

        ////Random of the lowest 2 directions : Returns random of the 2 lowest directions
        Vector2Int newDirection = UnityEngine.Random.Range(0, 2) == 1 ? minDirection1 : minDirection2;

        ////Random of the lowest 2 directions with closest direction being more likely
        //float totalTheta = (Vector2.Dot((Vector2)minDirection1, (Vector2)minDirection2) - 1) * -1;
        //float currDirTheta = (Vector2.Dot((Vector2)minDirection1, (Vector2)currDirection) - 1) * -1;
        //Vector2Int newDirection = UnityEngine.Random.Range(0, totalTheta + 0.1f) > currDirTheta ? minDirection1 : minDirection2;

        return newDirection;
    }

    private float calculateAvarageHeight(int x, int y)
    {
        float totalHeight = _currNoise[x, y];
        totalHeight += _currNoise[x - 1, y - 1];
        totalHeight += _currNoise[x, y - 1];
        totalHeight += _currNoise[x + 1, y - 1];
        totalHeight += _currNoise[x - 1, y];
        totalHeight += _currNoise[x + 1, y];
        totalHeight += _currNoise[x - 1, y + 1];
        totalHeight += _currNoise[x, y + 1];
        totalHeight += _currNoise[x + 1, y + 1];

        totalHeight /= 9f;

        return totalHeight; 
    }

    private float getNeighborHeightChange(Vector2Int point)
    {
        Vector3Int off = new Vector3Int(1, 1, 0);
        float hC = _terrain.terrainData.GetHeight(point.y, point.x);
        float hL = _terrain.terrainData.GetHeight(point.y, point.x - off.x);
        float hR = _terrain.terrainData.GetHeight(point.y, point.x + off.x);
        float hD = _terrain.terrainData.GetHeight(point.y - off.y, point.x);
        float hU = _terrain.terrainData.GetHeight(point.y + off.y, point.x);

        //Adds up the difference in height between the neighbors
        float totalHightChange = Mathf.Abs(hL - hC) + Mathf.Abs(hR - hC) + Mathf.Abs(hD - hC) + Mathf.Abs(hU - hC);

        return totalHightChange;
    }

    private void driveCar()
    {
        Unity.Mathematics.float3 position;
        Unity.Mathematics.float3 forward;
        Unity.Mathematics.float3 up;

        //Gets position, forward, and up vectors for the car at the current t value in the spline
        _currSplineContainer.Evaluate(_carTime, out position, out forward, out up);

        //Loops _carTime from 0-1
        _carTime += _carSpeedDampener * _carSpeed;
        if (_carTime > 1)
        {
            _carTime = 0;
        }

        Unity.Mathematics.float3 carOffset = new Vector3(0, _roadOffsetY + _currCar.transform.lossyScale.y * 0.5f, 0);

        //Offsets car position from the road and makes sure it is facing the correct way
        if (_currCar != null)
        {
            _currCar.transform.position = position + carOffset;
            _currCar.transform.LookAt(_currCar.transform.position + (Vector3)forward);
        }

        //Sets camera location reletive to car
        Vector3 camLookOffset = new Vector3(Mathf.Cos(Time.time * 0.3f), Mathf.Cos(Time.time * 0.2f) * 0.2f, Mathf.Sin(Time.time * 0.3f)); //Circling offset
        Vector3 camPos = _currCar.transform.position + (Vector3)up * 0.5f + camLookOffset; //Raises camera above car with circling offset

        //Moves cam behind car if true
        if (!_keepCamBehindCar)
        {
            camPos -= ((Vector3)forward).normalized * 2f;
        }

        Vector3 camDirToCar = (_currCar.transform.position - camPos).normalized;

        camPos -= camDirToCar * 4; //Moves camera away from center of the car
        Camera.main.transform.position  = camPos + new Vector3(0, Mathf.Abs(Vector3.Dot(up, Vector3.right)), 0) * 6; //Raises camera if car is moving on a slope to keep the cam from being in the ground
        Camera.main.transform.LookAt(_currCar.transform.position); //Makes sure camera is looking at the car
    }

    //Creates a visual to see the exact points the A* path returns, connected by line rendererd
    private void GeneratePointConnectorVisual(List<Vector2Int> path)
    {
        Vector3 scale = _terrain.terrainData.heightmapScale;

        //Clears previous line renderers
        LineRenderer[] lineRenderers = FindObjectsOfType<LineRenderer>();
        foreach (LineRenderer lr in lineRenderers)
        {
            Destroy(lr.gameObject);
        }

        //Runs through each point and makes a line between them, adding up total path length
        float totalLength = 0;
        for (int i = 0; i < path.Count - 1; i++)
        {

            GameObject empty = new GameObject();
            LineRenderer line = empty.AddComponent<LineRenderer>();

            //Converts points into world space
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

    //Generates the spline for the road
    private void GenerateRoadSpline(List<Vector2Int> path)
    {
        Vector3 scale = _terrain.terrainData.heightmapScale;

        //Clears previous splines
        SplineContainer[] splineContainers = FindObjectsOfType<SplineContainer>();
        foreach (SplineContainer sc in splineContainers)
        {
            Destroy(sc.gameObject);
        }

        SplineContainer spline = Instantiate(_spline).GetComponent<SplineContainer>();
        _currSplineContainer = spline;

        //Adds each point on the path as a knot to the spline (in reverse order since the path is already in reverse)
        for (int i = path.Count - 1; i >= 0; i--)
        {
            spline.Spline.Add(new BezierKnot(new Vector3(path[i].x * scale.x, _currNoise[path[i].y, path[i].x] * scale.y, path[i].y * scale.z)), 0);
        }

        //Dissables spline renderer to have road mesh render instead
        spline.GetComponent<MeshRenderer>().enabled = false;
    }

    public void GenerateRoad(List<Vector2Int> path)
    {
        GenerateRoadSpline(path);

        //Generates road mesh
        _currRoadMeshFilter.mesh = RoadGenerator.GenerateRoadMesh(_currSplineContainer, _roadWidth, _splineResolution);
        _currRoadMeshFilter.transform.position = new Vector3(0, _roadOffsetY, 0); //Offsets mesh from original points to be slightly above original spline 
    }
}
