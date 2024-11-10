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
        public AStarNode(Vector2Int p)
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

    public List<Vector2Int> generatePath(float[,] noise, Vector2Int startPoint, Vector2Int goalPoint, int gridMaskSize = 5, int minSegmentSize = 1, float maxSlope = 1)
    {
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();  
        frontier = new PriorityQueue<AStarNode, float>();                  
        HashSet<Vector2Int> frontierSet = new HashSet<Vector2Int>();       
        Dictionary<Vector2Int, bool> visited = new Dictionary<Vector2Int, bool>();    
        int totalNeighborsEvaluated = 0;
        _maxSlope = maxSlope;

        //Sets up start node
        AStarNode start = new AStarNode(startPoint);
        start.heuristic = start.calculateEuclideanHeuristic(goalPoint, noise);
        frontier.Enqueue(start, start.getTotalWeight());
        frontierSet.Add(start.centerPoint);
        Vector2Int endPoint = Vector2Int.zero;

        while (frontier.Count > 0)
        {
            AStarNode currNode = frontier.Dequeue(); //Gets the current node from the priority queue
            frontierSet.Remove(currNode.centerPoint); //Removes the current node point from the fronteirSet

            endPoint = currNode.centerPoint; //Keeps track of last point evaluated
            if (currNode.centerPoint == goalPoint) //Checks if goal and breaks
            {
                endPoint = goalPoint;
                break;
            }

            visited[currNode.centerPoint] = true; //Sets the current point as visited

            //Gest neighbors from gridmask
            List<Vector2Int> neighbors = getGridMaskPoints(gridMaskSize, minSegmentSize, noise, currNode.centerPoint, frontierSet, visited); //Gets the neighbors of the current point
            totalNeighborsEvaluated += neighbors.Count;
            Debug.Log("Ne: " + neighbors.Count);

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
                    
                    newNeighborNode.heuristic = newNeighborNode.calculateEuclideanHeuristic(goalPoint, noise); //Calculates node heuristic

                    //Adds neighbor to priority queue and frontier set
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
        
        path.Add(start.centerPoint); //Adds start to path

        return path;
    }

    List<Vector2Int> getGridMaskPoints(int maskSize, int minSegmentSize, float[,] noise, Vector2Int currPoint, HashSet<Vector2Int> frontierSet, Dictionary<Vector2Int, bool> visited)
    {
        List<Vector2Int> neighbors = new List<Vector2Int>();

        //Runs through all the points on the neighbor grid (Not super efficient)
        for (int j = -maskSize; j <= maskSize; j++)
        {
            for (int i = -maskSize; i <= maskSize; i++)
            {
                int worldPointX = currPoint.x + i;
                int worldPointY = currPoint.y + j;

                //Makes sure point is not already visited and not already in the frontier
                if (!visited.ContainsKey(new Vector2Int(worldPointX, worldPointY)) && !frontierSet.Contains(new Vector2Int(worldPointX, worldPointY)))
                {
                    //Makes sure point is within the grid
                    if (worldPointX >= 0 && worldPointY >= 0 && worldPointX < noise.GetLength(0) && worldPointY < noise.GetLength(1))
                    {
                        //Only checks neighbors that are above the minimum segment size on the grid
                        if (i * i >= minSegmentSize * minSegmentSize || j * j >= minSegmentSize * minSegmentSize)
                        {
                            //The first part with modulus only evaluates neighbors on the grid with unique directions (no repeat directions)
                            // the second part with == 1 is to return true since anything modulus'ed with 1 is 0 (Ex. -3, 1)
                            // the third part is to get all the directions at the minsegment level
                            if ((i != 0 && j != 0 && i % j != 0 && j % i != 0) || (i * i == 1 || j * j == 1) || (i * i == minSegmentSize * minSegmentSize || j * j == minSegmentSize * minSegmentSize))
                            {
                                Vector2Int worldPoint = new Vector2Int(worldPointX, worldPointY);

                                //Calculates slope of neighbor
                                float deltaHeight = noise[worldPointY, worldPointX] - noise[currPoint.y, currPoint.x];
                                float distance = (worldPoint - currPoint).magnitude;
                                float slope = deltaHeight / (distance / noise.GetLength(0));

                                //Only adds neighbor if slope is within valid range
                                if (-_maxSlope < slope && slope < _maxSlope)
                                {
                                    neighbors.Add(worldPoint);
                                }
                            }
                        }
                    }
                }
            }
        }

        return neighbors;
    }
}
