using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using MediaBrowser;

namespace Diamond.Code
{
    internal class OnScreenHelp
    {
        public OnScreenHelp()
        {
            Application.CurrentInstance.Information.AddInformationString("HI ONSCREEN HELP 1");
            Application.CurrentInstance.Information.AddInformationString("HI ONSCREEN HELP 2");
            Application.CurrentInstance.Information.AddInformationString("HI ONSCREEN HELP 3");
            Application.CurrentInstance.Information.AddInformationString("HI ONSCREEN HELP 4");
        }
    }
}
