namespace Blobin.Core
{
    /// <summary>
    /// マスクのモルフォロジー演算モード
    /// </summary>
    public enum MorphologyMode
    {
        [System.ComponentModel.DataAnnotations.Display(Name = "膨張 (Dilate)")]
        Dilate,
        [System.ComponentModel.DataAnnotations.Display(Name = "クロージング (Closing)")]
        Closing,
        [System.ComponentModel.DataAnnotations.Display(Name = "オープニング (Opening)")]
        Opening,
        [System.ComponentModel.DataAnnotations.Display(Name = "なし")]
        None,
    }

    /// <summary>
    /// 重複ブロブの処理モード (HL_Blobox 準拠)
    /// </summary>
    public enum OverlapMode
    {
        [System.ComponentModel.DataAnnotations.Display(Name = "そのまま")]
        Keep,
        [System.ComponentModel.DataAnnotations.Display(Name = "小さい方を除去")]
        RemoveSmaller,
        [System.ComponentModel.DataAnnotations.Display(Name = "大きい方を除去")]
        RemoveBigger,
        [System.ComponentModel.DataAnnotations.Display(Name = "領域を結合")]
        Merge,
    }

    /// <summary>
    /// ブロブ検出の方式
    /// </summary>
    public enum DetectionEngine
    {
        [System.ComponentModel.DataAnnotations.Display(Name = "従来エンジン")]
        Legacy,
        [System.ComponentModel.DataAnnotations.Display(Name = "HLSLエンジン")]
        Hlsl,
    }

    public enum DetectionMode
    {
        /// <summary>アルファ値</summary>
        Alpha,
        /// <summary>明るさ（明るい部分 / 暗い部分）</summary>
        Brightness,
        /// <summary>指定したキー色との近さ</summary>
        KeyColor,
        /// <summary>前フレームとの差分（動き）</summary>
        MotionDiff,
        /// <summary>RGB成分（赤 / 緑 / 青）</summary>
        RgbChannel,
        /// <summary>彩度（高彩度 / 低彩度）</summary>
        Saturation,
        /// <summary>色相範囲</summary>
        Hue,
        /// <summary>輪郭（エッジ検出）</summary>
        Edge,
    }

    /// <summary>
    /// しきい値の向き（「～より明るい」か「～より暗い」か等）
    /// </summary>
    public enum ThresholdDirection
    {
        /// <summary>しきい値以上を検出（明るい部分 / 高彩度 等）</summary>
        Above,
        /// <summary>しきい値以下を検出（暗い部分 / 低彩度 等）</summary>
        Below,
    }

    /// <summary>
    /// RGB成分検出で使用する色成分
    /// </summary>
    public enum RgbChannelType
    {
        Red,
        Green,
        Blue,
    }

    /// <summary>
    /// ブロブの表示形状
    /// </summary>
    public enum BlobShape
    {
        /// <summary>四角形</summary>
        Rectangle,
        /// <summary>角丸四角形</summary>
        RoundedRectangle,
        /// <summary>クロップ風（四隅のL字マーク）</summary>
        Crop,
        /// <summary>鉤括弧風（【 】のような左右の括弧）</summary>
        Bracket,
        /// <summary>円形</summary>
        Circle,
        /// <summary>多角形（輪郭に沿った多角形）</summary>
        Polygon,
        /// <summary>ライン表示（輪郭線のみ）</summary>
        Line,
    }

    /// <summary>
    /// ブロブ同士の接続方式
    /// </summary>
    public enum ConnectionMode
    {
        /// <summary>すべてのブロブを接続</summary>
        All,
        /// <summary>近いブロブ同士のみ接続</summary>
        Nearest,
    }

    /// <summary>
    /// 接続線のスタイル
    /// </summary>
    public enum ConnectionLineStyle
    {
        /// <summary>直線</summary>
        Straight,
        /// <summary>曲線</summary>
        Curve,
        /// <summary>スプライン風（S字の滑らかな曲線）</summary>
        Spline,
        /// <summary>点線</summary>
        Dotted,
        /// <summary>破線</summary>
        Dashed,
    }

    /// <summary>
    /// 矢印を表示する位置
    /// </summary>
    public enum ArrowPosition
    {
        None,
        Start,
        End,
        Both,
    }

    /// <summary>
    /// ラベルの表示位置
    /// </summary>
    public enum LabelPosition
    {
        Top,
        Bottom,
        Left,
        Right,
        Center,
        Inside,
    }

    /// <summary>
    /// マーカーの形状
    /// </summary>
    public enum MarkerShape
    {
        Dot,
        Cross,
        Plus,
        Diamond,
        Square,
        Ring,
    }

    /// <summary>
    /// ブロブ内エフェクトの種類
    /// </summary>
    public enum InBlobEffectType
    {
        /// <summary>効果なし</summary>
        None,
        /// <summary>色反転</summary>
        Invert,
        /// <summary>色相回転</summary>
        HueRotate,
        /// <summary>色味変更（チャンネルシフト）</summary>
        ColorShift,
        /// <summary>グリッチ（走査線ずらし）</summary>
        Glitch,
        /// <summary>ブロック複製（モザイク状の複製）</summary>
        BlockDuplicate,
        /// <summary>滲み（ぼかし）</summary>
        Bleed,
        /// <summary>斜線</summary>
        DiagonalStripe,
        /// <summary>時間ずらし（過去フレームの表示）</summary>
        TimeOffset,
        /// <summary>ブロブごとにランダムなエフェクトを適用</summary>
        Random,
    }
}
