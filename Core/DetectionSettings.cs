using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace Blobin.Core
{
    /// <summary>
    /// ブロブ検出に関する設定項目。
    /// 映像内の特徴を解析し、条件に一致する領域をブロブとして検出するための各種パラメーターを保持する。
    /// </summary>
    public class DetectionSettings : Animatable
    {

        /// <summary>
        /// 検出方式
        /// </summary>
        [Display(GroupName = "検出", Name = "検出方式", Description = "映像のどの特徴でブロブを検出するかを指定します")]
        [EnumComboBox]
        public DetectionMode Mode { get => mode; set => Set(ref mode, value); }
        DetectionMode mode = DetectionMode.Brightness;

        /// <summary>
        /// しきい値の向き（明るい/暗い、高彩度/低彩度 等）
        /// </summary>
        [Display(GroupName = "検出", Name = "検出の向き", Description = "しきい値より上を検出するか下を検出するか（明るい部分/暗い部分 等）")]
        [EnumComboBox]
        public ThresholdDirection Direction { get => direction; set => Set(ref direction, value); }
        ThresholdDirection direction = ThresholdDirection.Above;

        /// <summary>
        /// 大津の2値化によるしきい値自動決定（明るさ・明度検出時）
        /// </summary>
        [Display(GroupName = "検出", Name = "自動しきい値 (大津)", Description = "映像全体の明暗ヒストグラムから最適な二値化しきい値を毎フレーム自動決定します")]
        [ShowPropertyEditorWhen(nameof(Mode), DetectionMode.Brightness)]
        [ToggleSlider]
        public bool UseOtsu { get => useOtsu; set => Set(ref useOtsu, value); }
        bool useOtsu = false;

        /// <summary>
        /// しきい値（0～100）
        /// </summary>
        [Display(GroupName = "検出", Name = "しきい値", Description = "ブロブとして検出する条件のしきい値")]
        [ShowPropertyEditorWhen(nameof(UseOtsu), false)]
        [AnimationSlider("F0", "", 0, 500)]
        public Animation Threshold { get; } = new Animation(50, 0, 100);

        /// <summary>
        /// キー色検出時に使用する許容範囲（キー色との色差の許容量）
        /// </summary>
        [Display(GroupName = "検出", Name = "キー色許容範囲", Description = "指定したキー色からどれだけ離れた色まで許容するか")]
        [ShowPropertyEditorWhen(nameof(Mode), DetectionMode.KeyColor)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation KeyColorTolerance { get; } = new Animation(20, 0, 100);

        /// <summary>
        /// 検出方式が「指定したキー色」の場合に使用する色
        /// </summary>
        [Display(GroupName = "検出", Name = "キー色", Description = "検出方式が「指定したキー色」のときに使用する基準色")]
        [ShowPropertyEditorWhen(nameof(Mode), DetectionMode.KeyColor)]
        [ColorPicker]
        public Color KeyColor { get => keyColor; set => Set(ref keyColor, value); }
        Color keyColor = Colors.Lime;

        /// <summary>
        /// 検出方式が「RGB成分」の場合に使用する成分
        /// </summary>
        [Display(GroupName = "検出", Name = "RGB成分", Description = "検出方式が「RGB成分」のときに使用する色成分")]
        [ShowPropertyEditorWhen(nameof(Mode), DetectionMode.RgbChannel)]
        [EnumComboBox]
        public RgbChannelType RgbChannel { get => rgbChannel; set => Set(ref rgbChannel, value); }
        RgbChannelType rgbChannel = RgbChannelType.Red;

        /// <summary>
        /// 検出方式が「色相範囲」の場合の色相範囲の開始（0～360度）
        /// </summary>
        [Display(GroupName = "検出", Name = "色相 開始", Description = "検出する色相範囲の開始角度")]
        [ShowPropertyEditorWhen(nameof(Mode), DetectionMode.Hue)]
        [AnimationSlider("F0", "°", 0, 360)]
        public Animation HueMin { get; } = new Animation(0, 0, 360);

        /// <summary>
        /// 検出方式が「色相範囲」の場合の色相範囲の終了（0～360度）
        /// </summary>
        [Display(GroupName = "検出", Name = "色相 終了", Description = "検出する色相範囲の終了角度")]
        [ShowPropertyEditorWhen(nameof(Mode), DetectionMode.Hue)]
        [AnimationSlider("F0", "°", 0, 360)]
        public Animation HueMax { get; } = new Animation(60, 0, 360);

        /// <summary>
        /// 動き差分検出の感度
        /// </summary>
        [Display(GroupName = "検出", Name = "動き検出感度", Description = "検出方式が「動きの差分」のときの感度")]
        [ShowPropertyEditorWhen(nameof(Mode), DetectionMode.MotionDiff)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation MotionSensitivity { get; } = new Animation(30, 0, 100);

        /// <summary>
        /// 検出の詳細設定を表示するかどうか
        /// </summary>
        [Display(GroupName = "検出", Name = "詳細設定を表示", Description = "検出エンジン、面積範囲、解析精度、ノイズ除去、安定化などの詳細設定を表示します")]
        [ToggleSlider]
        public bool ShowAdvanced { get => showAdvanced; set => Set(ref showAdvanced, value); }
        bool showAdvanced = false;

        /// <summary>
        /// 検出エンジン
        /// </summary>
        [Display(GroupName = "検出", Name = "検出エンジン", Description = "従来エンジンまたはGPUシェーダーを用いた高速HLSLエンジンを選択します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [EnumComboBox]
        public DetectionEngine Engine { get => engine; set => Set(ref engine, value); }
        DetectionEngine engine = DetectionEngine.Hlsl;

        /// <summary>
        /// 最小面積（出力解像度に対する割合、%）
        /// </summary>
        [Display(GroupName = "検出", Name = "最小面積", Description = "ブロブとして採用する最小の面積（画面全体に対する割合）")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F2", "%", 0, 100)]
        public Animation MinArea { get; } = new Animation(0.05, 0, 100);

        /// <summary>
        /// 最大面積（出力解像度に対する割合、%）
        /// </summary>
        [Display(GroupName = "検出", Name = "最大面積", Description = "ブロブとして採用する最大の面積（画面全体に対する割合）")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F2", "%", 0, 100)]
        public Animation MaxArea { get; } = new Animation(40, 0, 100);

        /// <summary>
        /// ブロブの外接枠が画面全体に占める割合の上限（%）
        /// </summary>
        [Display(GroupName = "検出", Name = "最大枠占有率", Description = "ブロブの囲み枠（外接矩形）が画面全体に占める割合の上限")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation MaxBoundingBoxArea { get; } = new Animation(50, 0, 100);

        /// <summary>
        /// 画面端（枠）に接している背景等の大きなブロブを除外するか
        /// </summary>
        [Display(GroupName = "検出", Name = "画面端ブロブを除外", Description = "画面枠（端）に接している背景などの大きなブロブを除外します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [ToggleSlider]
        public bool IgnoreEdgeBlobs { get => ignoreEdgeBlobs; set => Set(ref ignoreEdgeBlobs, value); }
        bool ignoreEdgeBlobs = true;

        /// <summary>
        /// 最大ブロブ数
        /// </summary>
        [Display(GroupName = "検出", Name = "最大ブロブ数", Description = "同時に検出するブロブの最大数。面積の大きい順に採用されます")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [TextBoxSlider("F0", "個", 1, 64)]
        [DefaultValue(16)]
        [Range(1, 256)]
        public int MaxBlobCount { get => maxBlobCount; set => Set(ref maxBlobCount, value); }
        int maxBlobCount = 16;

        /// <summary>
        /// 解析精度。値が大きいほど細かく解析するが処理は重くなる。
        /// 内部では長辺がこのピクセル数になるように画面を縮小して解析する。
        /// </summary>
        [Display(GroupName = "検出", Name = "解析精度", Description = "解析用に縮小する際の長辺ピクセル数。大きいほど高精度・低速になります")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [TextBoxSlider("F0", "px", 32, 960)]
        [DefaultValue(240)]
        [Range(16, 1920)]
        public int AnalysisResolution { get => analysisResolution; set => Set(ref analysisResolution, value); }
        int analysisResolution = 240;

        /// <summary>
        /// 検出結果を左右反転して評価するか等、隣接ピクセルの誤検出を減らす軽い平滑化を行うか
        /// </summary>
        [Display(GroupName = "検出", Name = "ノイズ除去", Description = "小さなノイズ状の検出を軽減します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [ToggleSlider]
        public bool DenoiseEnabled { get => denoiseEnabled; set => Set(ref denoiseEnabled, value); }
        bool denoiseEnabled = true;

        /// <summary>
        /// 重なり合ったブロブの処理モード
        /// </summary>
        [Display(GroupName = "検出", Name = "重なり処理", Description = "ブロブ同士が重なっている場合の処理（そのまま / 小さい方を除去 / 大きい方を除去 / 結合）")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [EnumComboBox]
        public OverlapMode Overlap { get => overlap; set => Set(ref overlap, value); }
        OverlapMode overlap = OverlapMode.Keep;

        /// <summary>
        /// 検出前の平滑化（ブラー半径）
        /// </summary>
        [Display(GroupName = "検出", Name = "前処理ブラー", Description = "二値化前に画像をぼかすことで細かいチラつきノイズを抑制します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "px", 0, 20)]
        public Animation BlurRadius { get; } = new Animation(1, 0, 20);

        /// <summary>
        /// モルフォロジー演算の種類
        /// </summary>
        [Display(GroupName = "検出", Name = "モルフォロジー演算", Description = "二値化マスクの整形（膨張: 穴埋め / クロージング: 結合と穴埋め / オープニング: ノイズ除去）")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [EnumComboBox]
        public MorphologyMode Morphology { get => morphology; set => Set(ref morphology, value); }
        MorphologyMode morphology = MorphologyMode.Dilate;

        /// <summary>
        /// マスク膨張（モルフォロジー演算 Dilate）
        /// </summary>
        [Display(GroupName = "検出", Name = "マスク膨張/半径", Description = "モルフォロジー演算の適用半径 (px)")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "px", 0, 10)]
        public Animation DilateRadius { get; } = new Animation(1, 0, 10);

        /// <summary>
        /// フレーム間の位置・大きさの揺れ（荒ぶり）を抑える平滑化の強さ
        /// </summary>
        [Display(GroupName = "検出", Name = "安定化", Description = "ブロブの位置や大きさがフレームごとにガタつくのを抑えます。値を上げるほど滑らかになりますが、実際の動きへの追従は遅くなります")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Smoothing { get; } = new Animation(60, 0, 100);

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
            [Threshold, KeyColorTolerance, HueMin, HueMax, MotionSensitivity, MinArea, MaxArea, MaxBoundingBoxArea, BlurRadius, DilateRadius, Smoothing];
    }
}
