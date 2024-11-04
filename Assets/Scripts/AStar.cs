using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Utils;

public class AStar
{
    private PriorityQueue<AStarNode, float> frontier;
    private float _maxSlope = 1f;

    private struct AStarNode
    {
        public AStarNode(Vector2Int p) //Path segment mask
        {
            centerPoint = p;
            weight = 0;
            heuristic = 0;
        }

        public Vector2Int centerPoint;
        public float weight;
        public float heuristic;
        public static bool operator <(AStarNode a, AStarNode b) {
            return (a.weight + a.heuristic) > (b.weight + b.heuristic);
        }

        public static bool operator >(AStarNode a, AStarNode b)
        {
            return (a.weight + a.heuristic) < (b.weight + b.heuristic);
        }

        public float getTotalWeight() { return weight + heuristic; }

        public float calculateEuclideanHeuristic(Vector2Int goal, float[,] noise) {
            //Calculates heuristic by converting positions to a unit cube (since the noise values are from 0-1)
            //and then finds the magnatude of the displacment vector
            Vector2 scaledGoalXYPos = (Vector2)goal / noise.GetLength(0);
            Vector2 scaledCurrentXYPos = (Vector2)centerPoint / noise.GetLength(0);
            Vector3 worldDisVec = new Vector3(scaledGoalXYPos.x, noise[goal.y, goal.x], scaledGoalXYPos.y) - new Vector3(scaledCurrentXYPos.x, noise[centerPoint.y, centerPoint.x], scaledCurrentXYPos.y);
            return worldDisVec.magnitude;
        }
    }

    public List<Vector2Int> generatePath(float[,] noise, Vector2Int startPoint, Vector2Int goalPoint, int gridMaskSize = 5, float maxSlope = 1)
    {
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();  // to build the flowfield and build the path
        frontier = new PriorityQueue<AStarNode, float>();                   // to store next ones to visit
        HashSet<Vector2Int> frontierSet = new HashSet<Vector2Int>();        // OPTIMIZATION to check faster if a point is in the queue
        Dictionary<Vector2Int, bool> visited = new Dictionary<Vector2Int, bool>();      // use .at() to get data, if the element don't exist [] will give you wrong results
        int totalNeighborsEvaluated = 0;
        _maxSlope = maxSlope;

        // bootstrap state
        AStarNode start = new AStarNode(startPoint);
        start.heuristic = start.calculateEuclideanHeuristic(goalPoint, noise);

        //Adds the start node to the frontier and adds the point to the frontier set to keep track of it
        frontier.Enqueue(start, start.getTotalWeight());
        frontierSet.Add(start.centerPoint);
        Vector2Int endPoint = Vector2Int.zero;  // if at the end of the loop we don't find a border, we have to return random points

        while (frontier.Count > 0)
        {
            AStarNode currNode = frontier.Dequeue(); //Gets the current node from the priority queue
            frontierSet.Remove(currNode.centerPoint); //Removes the current node point from the fronteirSet

            if (currNode.centerPoint == goalPoint)
            {
                endPoint = goalPoint;
                break;
            }
            else
            {
                endPoint = currNode.centerPoint;
            }

            visited[currNode.centerPoint] = true; //Sets the current point as visited
            List<Vector2Int> neighbors = getGridMaskPoints(gridMaskSize, noise, currNode.centerPoint, frontierSet, visited); //Gets the neighbors of the current point
            totalNeighborsEvaluated += neighbors.Count;

            //While the neighbors exist, update the cameFrom map with them and add them to the priority queue
            if (neighbors.Count > 0)
            {
                foreach (Vector2Int neighbor in neighbors) {
                    cameFrom[neighbor] = currNode.centerPoint;
                    AStarNode newNeighborNode = new AStarNode(neighbor); //Converts neighbor point into AStar node

                    //Calculates weight by converting positions to a unit cube (since the noise values are from 0-1)
                    //and then finds the magnatude of the displacment vector which is added to the previous node's weight
                    Vector2 scaledNeighborXYPos = (Vector2)neighbor / noise.GetLength(0);
                    Vector2 scaledCurrentXYPos = (Vector2)currNode.centerPoint / noise.GetLength(0);
                    Vector3 worldDisVec = new Vector3(scaledNeighborXYPos.x, noise[neighbor.y, neighbor.x], scaledNeighborXYPos.y) - new Vector3(scaledCurrentXYPos.x, noise[currNode.centerPoint.y, currNode.centerPoint.x], scaledCurrentXYPos.y);
                    newNeighborNode.weight = currNode.weight + worldDisVec.magnitude; //Updates neighbor weights
                    
                    newNeighborNode.heuristic = newNeighborNode.calculateEuclideanHeuristic(goalPoint, noise);
                    frontier.Enqueue(newNeighborNode, newNeighborNode.getTotalWeight());
                    frontierSet.Add(neighbor);
                }
            }
        }

        Debug.Log("Total Neighbors Evaluated: " + totalNeighborsEvaluated);
        List<Vector2Int> path = new List<Vector2Int>(); //Path from the goal to the start

        if (endPoint != goalPoint)
        {
            Debug.Log("Did not find goal");
        }
        else
        {
            Debug.Log("Found Goal!!");
        }

        //Builds path only if a goal was found
        Vector2Int current = endPoint;
        while (current != start.centerPoint)
        { //Runs backwards through cameFrom map to build path from the goal to the start
            path.Add(current);
            current = cameFrom[current];
        }
        

        return path;
    }

    List<Vector2Int> getGridMaskPoints(int maskSize, float[,] noise, Vector2Int currPoint, HashSet<Vector2Int> frontierSet, Dictionary<Vector2Int, bool> visited)
    {
        List<Vector2Int> neighbors = new List<Vector2Int>();

        for (int j = -maskSize; j <= maskSize; j++)
        {
            for (int i = -maskSize; i <= maskSize; i++)
            {
                int worldPointX = currPoint.x + i;
                int worldPointY = currPoint.y + j;

                if (!visited.ContainsKey(new Vector2Int(worldPointX, worldPointY)) && !frontierSet.Contains(new Vector2Int(worldPointX, worldPointY)))
                {
                    if (worldPointX >= 0 && worldPointY >= 0 && worldPointX < noise.GetLength(0) && worldPointY < noise.GetLength(1))
                    {
                        if ((i != 0 && j != 0 && i % j != 0 && j % i != 0) || (i * i == 1 || j * j == 1))
                        {
                            float deltaHeight = noise[worldPointY, worldPointX] - noise[currPoint.y, currPoint.x];
                            float deltaX = worldPointX - currPoint.x;

                            float slope = deltaHeight / (deltaX / noise.GetLength(0));

                            if (-_maxSlope < slope && slope < _maxSlope)
                            {
                                neighbors.Add(new Vector2Int(worldPointX, worldPointY));
                            }
                        }
                    }
                }
            }
        }

        return neighbors;
    }
}
