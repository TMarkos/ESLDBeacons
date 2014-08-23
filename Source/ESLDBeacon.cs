using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ESLDCore
{
    public class ESLDBeacon : PartModule
    {
        public object targetbody;

        // Given a target body, get minimum ASL where beacon can function in km.
        public double findAcceptableAltitude(CelestialBody targetbody)
        {
            switch (beaconModel)
            {
                case "LB10":
                    gLimit = 1.0;
                    break;
                case "LB15":
                    gLimit = 0.5;
                    break;
                case "LB100":
                    gLimit = 0.1;
                    break;
                case "IB1":
                    gLimit = 0.1;
                    break;
                default:
                    gLimit = 0.1;
                    break;
            }
            if (hasSCU) gLimit *= 1.25;
            double limbo = Math.Round((Math.Sqrt((6.673E-11 * targetbody.Mass) / gLimit) - targetbody.Radius));
            if (limbo < targetbody.Radius * 0.25) limbo = targetbody.Radius * 0.25;
            return limbo / 1000;
        }

        // Display beacon status in right click menu.
        [KSPField(guiName = "Beacon Status", isPersistant = true, guiActive = true)]
        public string beaconStatus;

        [KSPField(isPersistant=true, guiActive=false)]
        public double gLimit = 2;

        [KSPField(isPersistant = true, guiActive = false)]
        public bool activated = false;

        [KSPField(isPersistant = true, guiActive = false)]
        public double fuelOnBoard = 0;

        [KSPField(isPersistant = true, guiActive = false)]
        public string beaconModel;

        [KSPField(isPersistant = true, guiActive = false)]
        public int techBoxInventory = 0;

        // Display beacon operational floor in right click menu.
        [KSPField(guiName = "Lowest Altitude", isPersistant = false, guiActive = true, guiUnits="km")]
        public double opFloor;

        public bool hasAMU = false;
        public bool hasHCU = false;
        public bool hasGMU = false;
        public bool hasSCU = false;

        public override void OnUpdate()
        {
            opFloor = findAcceptableAltitude(vessel.mainBody); // Keep updating tooltip display.
            ModuleAnimateGeneric MAG = part.FindModuleImplementing<ModuleAnimateGeneric>();
            MAG.Events["Toggle"].guiActive = false;
            if (activated && MAG.Progress == 0)
            {
                MAG.Toggle();
            } else if (!activated && MAG.Progress == 1)
            {
                MAG.Toggle();
            }
        }

        public void checkOwnTechBoxes()
        {
            hasAMU = false;
            hasHCU = false;
            hasGMU = false;
            hasSCU = false;
            techBoxInventory = 0;
            if (beaconModel == "IB1")
            {
                hasHCU = true;
                techBoxInventory += 2;
            }
            foreach (ESLDTechbox techbox in vessel.FindPartModulesImplementing<ESLDTechbox>())
            {
                if (techbox.activated)
                {
                    switch(techbox.techBoxModel)
                    {
                        case "AMU":
                            if (!hasAMU) techBoxInventory += 1;
                            hasAMU = true;
                            break;
                        case "HCU":
                            if (!hasHCU) techBoxInventory += 2;
                            hasHCU = true;
                            break;
                        case "GMU":
                            if (!hasGMU) techBoxInventory += 4;
                            hasGMU = true;
                            break;
                        case "SCU":
                            if (!hasSCU) techBoxInventory += 8;
                            hasSCU = true;
                            break;
                    }
                }
            }
        }

        // Startup sequence for beacon.
        [KSPEvent(name="BeaconInitialize", active = true, guiActive = true, guiName = "Initialize Beacon")]
        public void BeaconInitialize()
        {
            checkOwnTechBoxes();
            if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimit) // Check our G forces.
            {
                print("Too deep in gravity well to activate!");
                string thevar = "";
                if (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") thevar = "the ";
                ScreenMessages.PostScreenMessage("Cannot activate!  Gravity from " + thevar + vessel.mainBody.name + " is too strong.",5.0f,ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            }
            else if (vessel.altitude < (vessel.mainBody.Radius * .25f))
            {
                string thevar = "";
                if (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") thevar = "the ";
                ScreenMessages.PostScreenMessage("Cannot activate!  Beacon is too close to " + thevar + vessel.mainBody.name + ".", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            } else
            {
                ModuleAnimateGeneric MAG = part.FindModuleImplementing<ModuleAnimateGeneric>();
                print("Activating beacon!  Toggling MAG from " + MAG.status + "-" + MAG.Progress);
                MAG.Toggle();
                activated = true;
                part.force_activate();
                Events["BeaconInitialize"].active = false;
                Events["BeaconShutdown"].active = true;
                beaconStatus = "Active.";
            }
        }

        public override void OnFixedUpdate()
        {
            checkOwnTechBoxes();
            if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimit)
            {
                ScreenMessages.PostScreenMessage("Warning: Too deep in gravity well.  Beacon has been shut down for safety.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            }
            if (vessel.altitude < (vessel.mainBody.Radius * .25f))
            {
                string thevar = "";
                if (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") thevar = "the ";
                ScreenMessages.PostScreenMessage("Warning: Too close to " + thevar + vessel.mainBody.name + ".  Beacon has been shut down for safety.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            }
            double vfuel = 0;
            foreach (Part vpart in vessel.Parts)
            {
                foreach (PartResource vpr in vpart.Resources)
                {
                    if (vpr.resourceName == "Karborundum") vfuel += vpr.amount;
                }
            }
            fuelOnBoard = vfuel;
        }

        [KSPEvent(name = "BeaconShutdown", active = false, guiActive = true, guiName = "Shutdown")]
        public void BeaconShutdown()
        {
            ModuleAnimateGeneric MAG = part.FindModuleImplementing<ModuleAnimateGeneric>();
            print("Activating beacon!  Toggling MAG from " + MAG.status + "-" + MAG.Progress);
            MAG.Toggle();
            beaconStatus = "Offline.";
            part.deactivate();
            activated = false;
            Events["BeaconShutdown"].active = false;
            Events["BeaconInitialize"].active = true;
        }
    }
}
