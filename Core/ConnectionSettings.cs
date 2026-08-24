using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace Blobin.Core
{
    /// <summary>
    /// 検出したブロブ同士を接続する線の設定。
    /// ネットワーク図・解析画面のような表現を作るための項目。
    /// </summary>
    public class ConnectionSettings : Animatable
    {
        [Display(GroupName = "接続線", Name = "表示する", Description = "ブロブ同士を線で接続します")]
        [ToggleSlider]
        public bool Enabled { get => enabled; set => Set(ref enabled, value); }
        bool enabled = false;

        [Display(GroupName = "接続線", Name = "被写体色を反映", Description = "接続線の色に始点ブロブの実色（RGB）を自動適用します")]
        [ToggleSlider]
        public bool UseBlobColor { get => useBlobColor; set => Set(ref useBlobColor, value); }
        bool useBlobColor = false;

        [Display(GroupName = "接続線", Name = "色", Description = "接続線の色")]
        [ShowPropertyEditorWhen(nameof(UseBlobColor), false)]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        Color color = Color.FromRgb(0x30, 0xFF, 0xC0);

        [Display(GroupName = "接続線", Name = "太さ", Description = "接続線の太さ")]
        [AnimationSlider("F1", "px", 0, 10)]
        public Animation Width { get; } = new Animation(1.5, 0, 40);

        [Display(GroupName = "接続線", Name = "不透明度", Description = "接続線の不透明度")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Opacity { get; } = new Animation(70, 0, 100);

        [Display(GroupName = "接続線", Name = "詳細設定を表示", Description = "接続方式、接続距離、接続数制限、線のスタイル、矢印などの詳細設定を表示します")]
        [ToggleSlider]
        public bool ShowAdvanced { get => showAdvanced; set => Set(ref showAdvanced, value); }
        bool showAdvanced = false;

        [Display(GroupName = "接続線", Name = "接続方式", Description = "すべてのブロブを接続するか、近いブロブ同士のみ接続するか")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [EnumComboBox]
        public ConnectionMode Mode { get => mode; set => Set(ref mode, value); }
        ConnectionMode mode = ConnectionMode.Nearest;

        [Display(GroupName = "接続線", Name = "接続距離", Description = "「近いブロブ同士のみ接続」のときに接続する最大距離（画面対角線に対する割合）")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation MaxDistance { get; } = new Animation(35, 0, 200);

        [Display(GroupName = "接続線", Name = "接続数制限", Description = "1つのブロブから伸びる接続線の最大本数")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [TextBoxSlider("F0", "本", 1, 12)]
        [DefaultValue(3)]
        [Range(1, 64)]
        public int MaxConnectionsPerBlob { get => maxConnectionsPerBlob; set => Set(ref maxConnectionsPerBlob, value); }
        int maxConnectionsPerBlob = 3;

        [Display(GroupName = "接続線", Name = "線のスタイル", Description = "接続線の描画スタイル")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [EnumComboBox]
        public ConnectionLineStyle LineStyle { get => lineStyle; set => Set(ref lineStyle, value); }
        ConnectionLineStyle lineStyle = ConnectionLineStyle.Straight;

        [Display(GroupName = "接続線", Name = "曲がり具合", Description = "線のスタイルが曲線／スプライン風のときの曲がりの強さ")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation CurveStrength { get; } = new Animation(30, 0, 200);

        [Display(GroupName = "接続線", Name = "矢印の位置", Description = "矢印を表示する位置")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [EnumComboBox]
        public ArrowPosition ArrowPosition { get => arrowPosition; set => Set(ref arrowPosition, value); }
        ArrowPosition arrowPosition = ArrowPosition.None;

        [Display(GroupName = "接続線", Name = "矢印の大きさ", Description = "矢印の大きさ")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "px", 2, 40)]
        public Animation ArrowSize { get; } = new Animation(10, 0, 200);

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
            [MaxDistance, Width, Opacity, CurveStrength, ArrowSize];
    }
}
