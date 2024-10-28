using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Utils;

public class AStar
{
    private PriorityQueue<AStarNode, float> frontier;

    private struct AStarNode
    {
        public AStarNode(Vector2Int p) //Path segment mask
        {
            centerPoint = p;
            weight = 0;
            heuristic = 0;
        }

        public Vector2Int centerPoint;
        public int weight;
        public float heuristic;
        public static bool operator <(AStarNode a, AStarNode b) {
            return (a.weight + a.heuristic) > (b.weight + b.heuristic);
        }

        public static bool operator >(AStarNode a, AStarNode b)
        {
            return (a.weight + a.heuristic) < (b.weight + b.heuristic);
        }

        public float getTotalWeight() { return weight + heuristic; }

        public float calculateEuclideanHeuristic(Vector2 goal) {

            //Loop through all avalable directions to points that are not visited and calculate their weight and heuristic to the goal
            //Get the closest one / most likely one
            //Get rid of that direction for returning to this node

            //Calculate neighbors by drawing a vector to each point and if a vector exists with the same direction then replace that point with the closer one

            return (goal - centerPoint).magnitude;
        }
    }

    public List<Vector2Int> generatePath(float[,] noise, Vector2Int startPoint, Vector2Int goalPoint)
    {
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();  // to build the flowfield and build the path
        frontier = new PriorityQueue<AStarNode, float>();                   // to store next ones to visit
        HashSet<Vector2Int> frontierSet = new HashSet<Vector2Int>();        // OPTIMIZATION to check faster if a point is in the queue
        Dictionary<Vector2Int, bool> visited = new Dictionary<Vector2Int, bool>();      // use .at() to get data, if the element don't exist [] will give you wrong results

        // bootstrap state
        AStarNode start = new AStarNode(startPoint);
        start.heuristic = start.calculateEuclideanHeuristic(goalPoint);

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
            List<Vector2Int> neighbors = getGridMaskPoints(5, noise, currNode.centerPoint, frontierSet, visited); //Gets the neighbors of the current point

            //While the neighbors exist, update the cameFrom map with them and add them to the priority queue
            if (neighbors.Count > 0)
            {
                foreach (Vector2Int neighbor in neighbors) {
                    cameFrom[neighbor] = currNode.centerPoint;
                    AStarNode newNeighborNode = new AStarNode(neighbor); //Converts neighbor point into AStar node
                    newNeighborNode.weight = currNode.weight + 1; //Updates neighbor weights
                    newNeighborNode.heuristic = newNeighborNode.calculateEuclideanHeuristic(goalPoint);
                    frontier.Enqueue(newNeighborNode, newNeighborNode.getTotalWeight());
                    frontierSet.Add(neighbor);
                }
            }
        }

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
                            float deltaHeight = noise[worldPointX, worldPointY] - noise[currPoint.x, currPoint.y];
                            neighbors.Add(new Vector2Int(worldPointX, worldPointY));
                        }
                    }
                }
            }
        }

        return neighbors;
    }
}
