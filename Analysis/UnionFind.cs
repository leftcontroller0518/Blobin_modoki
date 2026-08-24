namespace Blobin.Analysis
{
    /// <summary>
    /// 連結成分（Connected Component）のラベリングに使用するUnion-Find（素集合データ構造）。
    /// </summary>
    internal sealed class UnionFind
    {
        readonly int[] parent;
        readonly int[] rank;

        public UnionFind(int size)
        {
            parent = new int[size];
            rank = new int[size];
            for (int i = 0; i < size; i++)
                parent[i] = i;
        }

        public int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        public void Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra == rb)
                return;

            if (rank[ra] < rank[rb])
                (ra, rb) = (rb, ra);

            parent[rb] = ra;
            if (rank[ra] == rank[rb])
                rank[ra]++;
        }
    }
}
