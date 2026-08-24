using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace Blobin.Core
{
    /// <summary>
    /// 検出したブロブに表示する情報ラベルの設定。
    /// </summary>
    public class LabelSettings : Animatable
    {
        [Display(GroupName = "ラベル", Name = "表示する", Description = "ブロブの情報ラベルを表示します")]
        [ToggleSlider]
        public bool Enabled { get => enabled; set => Set(ref enabled, value); }
        bool enabled = false;

        [Display(GroupName = "ラベル", Name = "ID", Description = "ブロブのIDを表示します")]
        [ToggleSlider]
        public bool ShowId { get => showId; set => Set(ref showId, value); }
        bool showId = true;

        [Display(GroupName = "ラベル", Name = "カスタムテキスト", Description = "表示する文字列。{id} {x} {y} {w} {h} {area} {speed} {color} が使用できます")]
        [TextEditor(AcceptsReturn = false)]
        public string CustomText { get => customText; set => Set(ref customText, value); }
        string customText = "";

        [Display(GroupName = "ラベル", Name = "被写体色を反映", Description = "検出した被写体の実色（RGB）をラベル文字の色に自動適用します")]
        [ToggleSlider]
        public bool UseBlobColor { get => useBlobColor; set => Set(ref useBlobColor, value); }
        bool useBlobColor = false;

        [Display(GroupName = "ラベル", Name = "色", Description = "ラベルの文字色")]
        [ShowPropertyEditorWhen(nameof(UseBlobColor), false)]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        Color color = Color.FromRgb(0x30, 0xFF, 0xC0);

        [Display(GroupName = "ラベル", Name = "文字サイズ", Description = "ラベルの文字サイズ")]
        [AnimationSlider("F0", "px", 6, 48)]
        public Animation FontSize { get; } = new Animation(14, 4, 200);

        [Display(GroupName = "ラベル", Name = "表示位置", Description = "ブロブに対するラベルの表示位置")]
        [EnumComboBox]
        public LabelPosition Position { get => position; set => Set(ref position, value); }
        LabelPosition position = LabelPosition.Top;

        [Display(GroupName = "ラベル", Name = "詳細設定を表示", Description = "座標・サイズ・面積の表示、フォントなどの詳細設定を表示します")]
        [ToggleSlider]
        public bool ShowAdvanced { get => showAdvanced; set => Set(ref showAdvanced, value); }
        bool showAdvanced = false;

        [Display(GroupName = "ラベル", Name = "座標", Description = "ブロブの座標(X, Y)を表示します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [ToggleSlider]
        public bool ShowCoordinate { get => showCoordinate; set => Set(ref showCoordinate, value); }
        bool showCoordinate = false;

        [Display(GroupName = "ラベル", Name = "幅・高さ", Description = "ブロブの幅と高さを表示します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [ToggleSlider]
        public bool ShowSize { get => showSize; set => Set(ref showSize, value); }
        bool showSize = false;

        [Display(GroupName = "ラベル", Name = "面積", Description = "ブロブの面積を表示します")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [ToggleSlider]
        public bool ShowArea { get => showArea; set => Set(ref showArea, value); }
        bool showArea = false;

        [Display(GroupName = "ラベル", Name = "フォント", Description = "ラベルのフォント")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [FontComboBox]
        public string Font { get => font; set => Set(ref font, value); }
        string font = "Consolas";

        protected override IEnumerable<IAnimatable> GetAnimatables() => [FontSize];
    }
}
