global using CColor = ChroniaHelper.Utils.ColorUtils.ChroniaColor;
global using ChroniaColor = ChroniaHelper.Utils.ColorUtils.ChroniaColor;
global using Clock = ChroniaHelper.Utils.StopwatchSystem.Stopclock;
global using Cons = ChroniaHelper.Utils.Constants;
global using GSav = ChroniaHelper.Modules.ChroniaHelperGlobalSaveData; // usual
global using HSL = ChroniaHelper.Utils.ColorUtils.HSLColor;
global using HSLColor = ChroniaHelper.Utils.ColorUtils.HSLColor;
global using HSV = ChroniaHelper.Utils.ColorUtils.HSVColor;
global using HSVColor = ChroniaHelper.Utils.ColorUtils.HSVColor;
global using MaP = ChroniaHelper.Cores.MapProcessor; // usual
global using Md = ChroniaHelper.ChroniaHelperModule; // usual
global using Prm = ChroniaHelper.Cores.Graphical.GraphicalParams;
global using PUt = ChroniaHelper.Utils.PlayerUtils;
global using Rd = ChroniaHelper.Utils.RandomUtils; // usual
global using Sav = ChroniaHelper.Modules.ChroniaHelperSaveData; // usual
global using Sens = ChroniaHelper.Utils.SensitiveFlags.Sensitivity;
global using Ses = ChroniaHelper.Modules.ChroniaHelperSession; // usual
global using Sts = ChroniaHelper.Modules.ChroniaHelperSettings; // usual
global using Vc2 = Microsoft.Xna.Framework.Vector2;
global using Vc3 = Microsoft.Xna.Framework.Vector3;
global using Ldm = ChroniaHelper.Cores.LoadingManager;
global using ExA = ChroniaHelper.Cores.ExtendedAttributes;

namespace ClimbUpHelper.Utils;

public static class Constants
{
    public const float piOverEight = MathHelper.PiOver4 / 2f;
    public const float eightPi = 4 * MathHelper.TwoPi;

    public const float HDScale = 6f;

    public static Vc2 VanillaCanvas = new Vc2(320f, 180f);

    public static Vc2 HDCanvas = new Vc2(1920f, 1080f);
}
