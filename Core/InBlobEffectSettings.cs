using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.ItemEditor.CustomVisibilityAttributes;

namespace Blobin.Core
{
    /// <summary>
    /// 検出したブロブの領域だけに適用するエフェクトの設定。
    /// </summary>
    public class InBlobEffectSettings : Animatable
    {
        [Display(GroupName = "ブロブ内エフェクト", Name = "適用する", Description = "検出したブロブの内側だけにエフェクトを適用します")]
        [ToggleSlider]
        public bool Enabled { get => enabled; set => Set(ref enabled, value); }
        bool enabled = false;

        [Display(GroupName = "ブロブ内エフェクト", Name = "種類", Description = "適用するエフェクトの種類。「ランダム」はブロブごとに異なるエフェクトを割り当てます")]
        [EnumComboBox]
        public InBlobEffectType EffectType { get => effectType; set => Set(ref effectType, value); }
        InBlobEffectType effectType = InBlobEffectType.Glitch;

        [Display(GroupName = "ブロブ内エフェクト", Name = "強さ", Description = "エフェクトの強さ")]
        [AnimationSlider("F0", "%", 0, 100)]
        public Animation Intensity { get; } = new Animation(60, 0, 100);

        [Display(GroupName = "ブロブ内エフェクト", Name = "詳細設定を表示", Description = "時間ずらし量、更新間隔、乱数シードなどの詳細設定を表示します")]
        [ToggleSlider]
        public bool ShowAdvanced { get => showAdvanced; set => Set(ref showAdvanced, value); }
        bool showAdvanced = false;

        [Display(GroupName = "ブロブ内エフェクト", Name = "時間ずらし量", Description = "「時間ずらし」のときに遡るフレーム数")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [TextBoxSlider("F0", "フレーム", 1, 60)]
        [DefaultValue(10)]
        [Range(1, 300)]
        public int TimeOffsetFrames { get => timeOffsetFrames; set => Set(ref timeOffsetFrames, value); }
        int timeOffsetFrames = 10;

        [Display(GroupName = "ブロブ内エフェクト", Name = "更新間隔", Description = "ランダム/グリッチ系エフェクトの模様を何フレームごとに更新するか")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [TextBoxSlider("F0", "フレーム", 1, 30)]
        [DefaultValue(4)]
        [Range(1, 240)]
        public int UpdateInterval { get => updateInterval; set => Set(ref updateInterval, value); }
        int updateInterval = 4;

        [Display(GroupName = "ブロブ内エフェクト", Name = "乱数シード", Description = "ランダム要素の種。値を変えると模様のパターンが変わります")]
        [ShowPropertyEditorWhen(nameof(ShowAdvanced), true)]
        [TextBoxSlider("F0", "", 0, 9999)]
        [DefaultValue(0)]
        [Range(0, int.MaxValue)]
        public int RandomSeed { get => randomSeed; set => Set(ref randomSeed, value); }
        int randomSeed = 0;

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Intensity];
    }
}
