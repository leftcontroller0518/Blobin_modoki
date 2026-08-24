using System.ComponentModel.DataAnnotations;
using Blobin.Core;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Blobin
{
    /// <summary>
    /// Blobin 映像エフェクト。
    /// 映像内の色・明るさ・輪郭・動きなどを解析してブロブ（特徴領域）を検出し、
    /// 枠・接続線・ラベル・マーカー・ブロブ内エフェクトを重ねて表示する。
    /// </summary>
    [VideoEffect("Blobin", ["解析", "テック"], ["ブロブ", "HUD", "スキャン", "グリッチ", "解析"])]
    public class BlobinVideoEffect : VideoEffectBase
    {
        public override string Label => "Blobin";

        // ── 各種設定 ──

        /// <summary>
        /// ブロブ検出設定
        /// </summary>
        [Display(GroupName = "Blobin", Name = "検出設定", Description = "ブロブ検出の設定", AutoGenerateField = true)]
        public DetectionSettings Detection { get => detection; set => Set(ref detection, value); }
        DetectionSettings detection = new();

        /// <summary>
        /// ブロブ表示設定
        /// </summary>
        [Display(GroupName = "Blobin", Name = "表示設定", Description = "ブロブの枠の表示設定", AutoGenerateField = true)]
        public StyleSettings Style { get => style; set => Set(ref style, value); }
        StyleSettings style = new();

        /// <summary>
        /// 接続線設定
        /// </summary>
        [Display(GroupName = "Blobin", Name = "接続線設定", Description = "ブロブ同士を接続する線の設定", AutoGenerateField = true)]
        public ConnectionSettings Connection { get => connection; set => Set(ref connection, value); }
        ConnectionSettings connection = new();

        /// <summary>
        /// ラベル設定
        /// </summary>
        [Display(GroupName = "Blobin", Name = "ラベル設定", Description = "ブロブに表示する情報ラベルの設定", AutoGenerateField = true)]
        public LabelSettings Label2 { get => label; set => Set(ref label, value); }
        LabelSettings label = new();

        /// <summary>
        /// マーカー設定
        /// </summary>
        [Display(GroupName = "Blobin", Name = "マーカー設定", Description = "ブロブ中心のマーカー設定", AutoGenerateField = true)]
        public MarkerSettings Marker { get => marker; set => Set(ref marker, value); }
        MarkerSettings marker = new();

        /// <summary>
        /// ブロブ内エフェクト設定
        /// </summary>
        [Display(GroupName = "Blobin", Name = "ブロブ内エフェクト設定", Description = "検出領域だけに適用するエフェクトの設定", AutoGenerateField = true)]
        public InBlobEffectSettings InBlobEffect { get => inBlobEffect; set => Set(ref inBlobEffect, value); }
        InBlobEffectSettings inBlobEffect = new();

        /// <summary>
        /// 元映像を表示するかどうか。オフにすると解析結果（枠・線・ラベル等）のみが表示される。
        /// </summary>
        [Display(GroupName = "Blobin", Name = "元映像を表示", Description = "オフにすると解析オーバーレイのみを表示します（監視カメラ風のUIのみを作りたい場合など）")]
        [ToggleSlider]
        public bool ShowSourceImage { get => showSourceImage; set => Set(ref showSourceImage, value); }
        bool showSourceImage = true;

        public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription)
        {
            //ブロブ検出はGPU上でのピクセル解析を必要とするため、AviUtl(.exo)出力には対応していない。
            return [];
        }

        public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices)
        {
            return new BlobinVideoEffectProcessor(devices, this);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() =>
            [Detection, Style, Connection, Label2, Marker, InBlobEffect];
    }
}
