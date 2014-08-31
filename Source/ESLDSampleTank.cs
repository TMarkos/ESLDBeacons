using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ESLDCore
{
    class ESLDSampleTank : PartModule, IPartCostModifier
    {
        [KSPField(isPersistant=true, guiActive=false)]
        public bool canisterSealed = true;
        [KSPField(isPersistant = false, guiActive = false)]
        public float markdown;  // Set to negative number in part.cfg file.

        public float GetModuleCost()
        {
            foreach (PartResource pR in part.Resources)
            {
                if (pR.amount < pR.maxAmount && canisterSealed)
                {
                    canisterSealed = false;
                }
            }
            if (!canisterSealed) return markdown;
            return 0.0f;
        }
    }
}
