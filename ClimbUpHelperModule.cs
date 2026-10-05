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
        }

        public override void Unload()
        {
        }
    }
}
