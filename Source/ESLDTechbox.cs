using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ESLDCore
{
    public class ESLDTechbox : PartModule
    {
        [KSPField(guiName = "Status", isPersistant = true, guiActive = true)]
        public string techBoxStatus;

        [KSPField(isPersistant = true, guiActive = false)]
        public bool activated = false;

        [KSPField(isPersistant = true, guiActive = false)]
        public string techBoxModel;

        public override void OnUpdate()
        {
            ModuleAnimateGeneric MAG = part.FindModuleImplementing<ModuleAnimateGeneric>();
            MAG.Events["Toggle"].guiActive = false;
            if (activated && MAG.Progress == 0)
            {
                MAG.Toggle();
            }
            else if (!activated && MAG.Progress == 1)
            {
                MAG.Toggle();
            }
        }

        [KSPEvent(name = "TechBoxOn", active = true, guiActive = true, guiName = "Activate")]
        public void TechBoxOn()
        {
            part.force_activate();
            activated = true;
            techBoxStatus = techBoxModel + " Active.";
            Events["TechBoxOn"].active = false;
            Events["TechBoxOff"].active = true;
            foreach (ESLDBeacon beacon in vessel.FindPartModulesImplementing<ESLDBeacon>())
            {
                beacon.checkOwnTechBoxes();
            }
        }

        [KSPEvent(name = "TechBoxOff", active = false, guiActive = true, guiName = "Deactivate")]
        public void TechBoxOff()
        {
            part.deactivate();
            activated = false;
            techBoxStatus = techBoxModel + " Inactive.";
            Events["TechBoxOn"].active = true;
            Events["TechBoxOff"].active = false;
            foreach (ESLDBeacon beacon in vessel.FindPartModulesImplementing<ESLDBeacon>())
            {
                beacon.checkOwnTechBoxes();
            }
        }
    }
}
