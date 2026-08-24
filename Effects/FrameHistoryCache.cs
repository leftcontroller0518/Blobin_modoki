using Blobin.Analysis;

namespace Blobin.Effects
{
    /// <summary>
    /// 「時間ずらし」エフェクト用に、過去数十フレーム分のバッファを保持するリングキャッシュ。
    /// 同一トラッキングIDでの追跡はせず、フレーム全体を縮小コピーして保持する簡易実装。
    /// </summary>
    public sealed class FrameHistoryCache
    {
        readonly LinkedList<FrameBuffer> history = [];
        int capacity;

        public FrameHistoryCache(int capacity = 300)
        {
            this.capacity = Math.Max(1, capacity);
        }

        public void SetCapacity(int capacity)
        {
            this.capacity = Math.Max(1, capacity);
            while (history.Count > this.capacity)
                history.RemoveFirst();
        }

        public void Push(FrameBuffer frame)
        {
            history.AddLast(frame);
            while (history.Count > capacity)
                history.RemoveFirst();
        }

        /// <summary>
        /// framesAgo フレーム前のバッファを取得する。存在しない場合は取得可能な最も古いフレーム、
        /// それも無ければ null を返す。
        /// </summary>
        public FrameBuffer? GetFrame(int framesAgo)
        {
            if (history.Count == 0)
                return null;

            int index = history.Count - 1 - framesAgo;
            index = Math.Clamp(index, 0, history.Count - 1);

            var node = history.First;
            for (int i = 0; i < index && node is not null; i++)
                node = node.Next;

            return node?.Value;
        }

        public void Clear() => history.Clear();
    }
}
