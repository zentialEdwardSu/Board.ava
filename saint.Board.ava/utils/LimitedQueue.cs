using System.Collections.Generic;

namespace saint.Board.ava.utils;

public class LimitedQueue<T> : Queue<T>
{
    private readonly int _capacity;
    
    public LimitedQueue(int capacity) => _capacity = capacity;
    
    public new void Enqueue(T item)
    {
        while (Count >= _capacity) Dequeue();
        base.Enqueue(item);
    }
}