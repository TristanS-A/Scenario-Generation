using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

//Tutorial I used: https://www.youtube.com/watch?v=ZiHH_BvjoGk&t=1s

public static class RoadGenerator
{
    private static void GetRoadWidthSegment(SplineContainer splineContainer, float roadWidth, float t, out Vector3 pos1, out Vector3 pos2)
    {
        Unity.Mathematics.float3 position;
        Unity.Mathematics.float3 forward;
        Unity.Mathematics.float3 up;
        splineContainer.Evaluate(t, out position, out forward, out up);

        Unity.Mathematics.float3 right = Vector3.Cross(forward, up).normalized;
        pos1 = position + roadWidth * right;
        pos2 = position - roadWidth * right;
    }

    public static void GetSplineVerts(SplineContainer splineContainer, float roadWidth, int splineResolution, ref List<Vector3> splineVertsP1, ref List<Vector3> splineVertsP2)
    {
        splineVertsP1 = new List<Vector3>();
        splineVertsP2 = new List<Vector3>();

        float step = 1f / (float)splineResolution;
        for (int i = 0; i < splineResolution; i++)
        {
            float time = step * i;
            GetRoadWidthSegment(splineContainer, roadWidth, time, out Vector3 pos1, out Vector3 pos2);
            splineVertsP1.Add(pos1);
            splineVertsP2.Add(pos2);
        }

    }

    public static Mesh GenerateRoadMesh(List<Vector3> splineVertsP1, List<Vector3> splineVertsP2)
    {
        Mesh roadMesh = new Mesh();
        List<Vector3> roadVerts = new List<Vector3>();
        List<int> roadTris = new List<int>();
        List<Vector2> roadUVs = new List<Vector2>();

        int offset = 0;
        float uvOffset = 0;
        int length = splineVertsP2.Count;

        for (int i = 1; i < length; i++)
        {
            Vector3 p1 = splineVertsP1[i - 1];
            Vector3 p2 = splineVertsP2[i - 1];
            Vector3 p3;
            Vector3 p4;

            p3 = splineVertsP1[i];
            p4 = splineVertsP2[i];

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
        return roadMesh;
    }
}
