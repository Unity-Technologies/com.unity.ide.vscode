using System.Collections.Generic;

namespace VSCodeEditor
{
    public readonly struct ManagedObjectRef<T>
        where T : class
    {
        public readonly int Id;

        public ManagedObjectRef(int id)
        {
            Id = id;
        }
    }

    public class ManagedObjectWorld
    {
        private int m_NextId;
        private readonly Dictionary<int, object> m_Objects;

        public ManagedObjectWorld(int initialCapacity = 1000)
        {
            m_NextId = 1;
            m_Objects = new Dictionary<int, object>(initialCapacity);
        }

        public ManagedObjectRef<T> Add<T>(T obj)
            where T : class
        {
            int id = m_NextId;
            m_NextId++;
            m_Objects[id] = obj;
            return new ManagedObjectRef<T>(id);
        }

        public T Get<T>(ManagedObjectRef<T> objRef)
            where T : class
        {
            return (T) m_Objects[objRef.Id];
        }

        public void Remove<T>(ManagedObjectRef<T> objRef)
            where T : class
        {
            m_Objects.Remove(objRef.Id);
        }
    }
}