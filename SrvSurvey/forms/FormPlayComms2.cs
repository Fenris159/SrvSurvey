using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valve.VR;

namespace SrvSurvey.forms;

[Draggable]
[System.ComponentModel.DesignerCategory("")]
internal class FormPlayComms2 : BaseFormZippy
{
    public static void toggleForm()
    {
        var form = BaseForm.get<FormPlayComms2>();
        if (form == null)
        {
            BaseForm.show<FormPlayComms2>();
        }
        else
        {
            if (Elite.focusSrvSurvey)
                form.Close();
            else
                form.Activate();
        }
    }

    public FormPlayComms2(): base()
    {
    }   

    protected override void initCtrls()
    {
        ctrls.Add(new BtnFillTextCtrl { r = new(20, 40, 0, 0), text = "Hello" });
        ctrls.Add(new BtnFillTextCtrl { r = new(60, 20, 0, 0), text = "Goodbye" });
        ctrls.Add(new BtnFillTextCtrl { r = new(120, 50, 0, 0), text = "Alpha" });
        ctrls.Add(new BtnFillTextCtrl { r = new(80, 120, 0, 0), text = "Bravo" });
    }
}