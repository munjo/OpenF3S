using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    internal class GameAccountSetting
    {
        public string[] MacroChat { get => _macroChat; }

        public string DefaultRoomTitle { get; set; }

        public int BGMVolume { get; set; }
        public int SFXVolume { get; set; }
        public int ScreenBrightness { get; set; }
        public bool IsHighGraphic { get; set; }
        public int QuickMathPlayer { get; set; }
        public int QuickMathStage { get; set; }
        public int QuickMathTierRestriction { get; set; }

        private string[] _macroChat = new string[8];
    }
}
