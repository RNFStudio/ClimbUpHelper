using System;

namespace ClimbUpHelper
{
    /// <summary>
    /// ClimbUpHelper 的模块主入口，负责 Mod 的加载与卸载。
    /// </summary>
    public class ClimbUpHelperModule : EverestModule
    {
        public static ClimbUpHelperModule Instance;

        public const string Name = "ClimbUpHelper";

        public override Type SettingsType => typeof(ClimbUpHelperSettings);
        public static ClimbUpHelperSettings Settings => (ClimbUpHelperSettings)Instance._Settings;

        public override Type SessionType => typeof(ClimbUpHelperSession);
        public static ClimbUpHelperSession Session => (ClimbUpHelperSession)Instance._Session;

        public override Type SaveDataType => typeof(ClimbUpHelperSaveData);
        public static ClimbUpHelperSaveData SaveData => (ClimbUpHelperSaveData)Instance._SaveData;

        public ClimbUpHelperModule()
        {
            Instance = this;
        }

        public override void Load()
        {
            // 在瀑布里阻止落地回复冲刺（临时把 NoRefills 设为 true，更新后恢复）
            On.Celeste.Player.Update += ChroniaHelper.Entities.WaterfallNoDashRefillController.Player_Update;
        }

        public override void Unload()
        {
            On.Celeste.Player.Update -= ChroniaHelper.Entities.WaterfallNoDashRefillController.Player_Update;
        }
    }
}
