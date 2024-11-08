using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Drop
{
    public Vector2 pos = Vector2Int.zero;
    public float volume = 1;
    public Vector2 speed = Vector2.zero;
    public float sediment = 0;
    public float pickup = 0.025f;

    public Drop(Vector2 pos)
    {
        this.pos = pos;
    }
}
