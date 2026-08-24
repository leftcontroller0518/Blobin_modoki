using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace Blobin.Core
{
    /// <summary>
    /// 検出したブロブの表示スタイル（枠の形状・色・線幅 等）
    /// </summary>
    public class StyleSettings : Animatable
    {
        [Display(GroupName = "ブロブ表示", Name = "表示する", Description = "検出したブロブの枠を表示します")]
        [ToggleSlider]
        public bool Enabled { get => enabled; set => Set(ref enabled, value); }
        bool enabled = true;

        [Display(GroupName = "ブロブ表示", Name = "形状", Description = "ブロブを囲む枠の形状")]
        [EnumComboBox]
        public BlobShape Shape { get => shape; set => Set(ref shape, value); }
        BlobShape shape = BlobShape.Bracket;

        [Display(GroupName = "ブロブ表示", Name = "被写体色を反映", Description = "検出した被写体の実色（RGB）を枠や塗りつぶしの色に自動適用します")]
        [ToggleSlider]
        public bool UseBlobColor { get => useBlobColor; set => Set(ref useBlobColor, value); }
        bool useBlobColor = false;

        [Display(GroupName = "ブロブ表示", Name = "色", Description = "枠線の色")]
        [ShowPropertyEditorWhen(nameof(UseBlobColor), false)]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        Color color = Color.FromRgb(0x30, 0xFF, 0xC0);

        [Display(GroupName = "ブロブ表示", Name = "線の太さ", Description = "枠線の太さ")]
        [AnimationSlider("F1", "px", 0, 20)]
        public Animation LineWidth { get; } = new Animation(2, 0, 40);

        [Display(GroupName = "ブロブ表示", Name = "不透明度", Description = "枠線の不透明度")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Opacity { get; } = new Animation(100, 0, 100);

        [Display(GroupName = "ブロブ表示", Name = "塗りつぶし", Description = "枠の内側を色で塗りつぶします")]
        [ToggleSlider]
        public bool FillEnabled { get => fillEnabled; set => Set(ref fillEnabled, value); }
        bool fillEnabled = false;

        [Display(GroupName = "ブロブ表示", Name = "詳細設定を表示", Description = "余白、角丸半径、コーナー長さ、塗りつぶし詳細設定を表示します")]
        [ToggleSlider]
        public bool ShowAdvanced { get => showAdvanced; set => Set(ref showAdvanced, value); }
        bool showAdvanced = false;

        [Display(GroupName = "ブロブ表示", Name = "余白", Description = "検出範囲に対して枠を広げる／狭める量")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "px", -50, 50)]
        public Animation Margin { get; } = new Animation(6, -500, 500);

        [Display(GroupName = "ブロブ表示", Name = "角丸半径", Description = "形状が角丸四角形のときの角の丸さ")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "px", 0, 60)]
        public Animation CornerRadius { get; } = new Animation(10, 0, 500);

        [Display(GroupName = "ブロブ表示", Name = "コーナー長さ", Description = "形状がクロップ風／鉤括弧風のときの角の線の長さ")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "px", 4, 80)]
        public Animation CornerLength { get; } = new Animation(16, 1, 500);

        [Display(GroupName = "ブロブ表示", Name = "塗りつぶし不透明度", Description = "塗りつぶしの不透明度")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation FillOpacity { get; } = new Animation(20, 0, 100);

        [Display(GroupName = "ブロブ表示", Name = "塗りつぶし最大面積", Description = "塗りつぶしを適用するブロブの最大画面占有率（これを超える大ブロブは枠線のみ表示します）")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation MaxFillArea { get; } = new Animation(25, 0, 100);

        [Display(GroupName = "ブロブ表示", Name = "端接ブロブの塗りつぶし除外", Description = "画面端に接するブロブの塗りつぶしを自動的に無効化します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [ToggleSlider]
        public bool ExcludeEdgeFill { get => excludeEdgeFill; set => Set(ref excludeEdgeFill, value); }
        bool excludeEdgeFill = true;

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
            [LineWidth, Opacity, Margin, CornerRadius, CornerLength, FillOpacity, MaxFillArea];
    }
}
