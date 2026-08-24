using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace Blobin.Core
{
    /// <summary>
    /// ブロブの中心などに表示するマーカーの設定。
    /// </summary>
    public class MarkerSettings : Animatable
    {
        [Display(GroupName = "マーカー", Name = "表示する", Description = "ブロブの中心にマーカーを表示します")]
        [ToggleSlider]
        public bool Enabled { get => enabled; set => Set(ref enabled, value); }
        bool enabled = false;

        [Display(GroupName = "マーカー", Name = "形状", Description = "マーカーの形状")]
        [EnumComboBox]
        public MarkerShape Shape { get => shape; set => Set(ref shape, value); }
        MarkerShape shape = MarkerShape.Cross;

        [Display(GroupName = "マーカー", Name = "被写体色を反映", Description = "検出した被写体の実色（RGB）をマーカーの色に自動適用します")]
        [ToggleSlider]
        public bool UseBlobColor { get => useBlobColor; set => Set(ref useBlobColor, value); }
        bool useBlobColor = false;

        [Display(GroupName = "マーカー", Name = "色", Description = "マーカーの色")]
        [ShowPropertyEditorWhen(nameof(UseBlobColor), false)]
        [ColorPicker]
        public Color Color { get => color; set => Set(ref color, value); }
        Color color = Colors.White;

        [Display(GroupName = "マーカー", Name = "大きさ", Description = "マーカーの大きさ")]
        [AnimationSlider("F0", "px", 2, 40)]
        public Animation Size { get; } = new Animation(8, 1, 200);

        [Display(GroupName = "マーカー", Name = "詳細設定を表示", Description = "マーカーの線の太さ、不透明度などの詳細設定を表示します")]
        [ToggleSlider]
        public bool ShowAdvanced { get => showAdvanced; set => Set(ref showAdvanced, value); }
        bool showAdvanced = false;

        [Display(GroupName = "マーカー", Name = "太さ", Description = "マーカーの線の太さ")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F1", "px", 0.5, 10)]
        public Animation LineWidth { get; } = new Animation(2, 0, 40);

        [Display(GroupName = "マーカー", Name = "不透明度", Description = "マーカーの不透明度")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Opacity { get; } = new Animation(100, 0, 100);

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Size, LineWidth, Opacity];
    }
}
