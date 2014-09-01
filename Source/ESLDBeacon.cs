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
        // Display beacon status in right click menu.
        [KSPField(guiName = "Beacon Status", isPersistant = true, guiActive = true)]
        public string beaconStatus;

        // Per-beacon G limit.
        [KSPField(isPersistant=true, guiActive=false)]
        public double gLimit = 2;

        // Activated state.
        [KSPField(isPersistant = true, guiActive = false)]
        public bool activated = false;

        // Self-reported fuel quantity.
        [KSPField(isPersistant = true, guiActive = false)]
        public double fuelOnBoard = 0;

        // Beacon model (from part config).
        [KSPField(isPersistant = true, guiActive = false)]
        public string beaconModel;

        // Self-reported binary techbox capability.  
        [KSPField(isPersistant = true, guiActive = false)]
        public int techBoxInventory = 0;

        // Display beacon operational floor in right click menu.
        [KSPField(guiName = "Lowest Altitude", isPersistant = false, guiActive = true, guiUnits = "km")]
        public double opFloor;

        // Charge to initialize beacon.
        [KSPField(guiName = "Charge to Activate", isPersistant = false, guiActive = false, guiUnits = " EC")]
        public double neededEC;

        // Charge to run beacon.
        [KSPField(guiName = "Electric Use", isPersistant = false, guiActive = false, guiUnits = " EC/s")]
        public double constantEC;

        public bool hasAMU = false;
        public bool hasHCU = false;
        public bool hasGMU = false;
        public bool hasSCU = false;

        public override void OnUpdate()
        {
            opFloor = findAcceptableAltitude(vessel.mainBody); // Keep updating tooltip display.
            if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude <= gLimit) Fields["neededEC"].guiActive = !activated;
            Fields["constantEC"].guiActive = activated;
            ModuleAnimateGeneric MAG = part.FindModuleImplementing<ModuleAnimateGeneric>();
            MAG.Events["Toggle"].guiActive = false;
            if (activated && MAG.Progress == 0)
            {
                MAG.Toggle();
            } else if (!activated && MAG.Progress == 1)
            {
                MAG.Toggle();
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

        public override void OnFixedUpdate()
        {
            checkOwnTechBoxes();
            if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimit)
            {
                ScreenMessages.PostScreenMessage("Warning: Too deep in gravity well.  Beacon has been shut down for safety.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            }
            if (vessel.altitude < (vessel.mainBody.Radius * 0.25f))
            {
                string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                ScreenMessages.PostScreenMessage("Warning: Too close to " + thevar + vessel.mainBody.name + ".  Beacon has been shut down for safety.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            }

            if (!requireResource(vessel, "ElectricCharge", TimeWarp.deltaTime * constantEC , true))
            {
                ScreenMessages.PostScreenMessage("Warning: Electric Charge depleted.  Beacon has been shut down.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            }

            if (!requireResource(vessel, "Karborundum", 0.1, false))
            {
                ScreenMessages.PostScreenMessage("Warning: Karborundum depleted.  Beacon has been shut down.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                BeaconShutdown();
            }
        }

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
            double neededMult = 10;
            double constantDiv = 50;
            if (hasGMU)
            {
                gLimit *= 1.25;
                neededMult = 15;
                constantDiv = 100 / 3;
            }
            double limbo = Math.Round((Math.Sqrt((6.673E-11 * targetbody.Mass) / gLimit) - targetbody.Radius));
            if (limbo < targetbody.Radius * 0.25) limbo = targetbody.Radius * 0.25;
            neededEC = Math.Round(fuelOnBoard * neededMult * (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude / gLimit));
            constantEC = Math.Round(fuelOnBoard / constantDiv * (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude / gLimit) * 100) / 100;
            return limbo / 1000;
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

        // Simple bool for resource checking and usage.  Returns true and optionally uses resource if resAmount of res is available.
        public bool requireResource(Vessel craft, string res, double resAmount, bool consumeResource)
        {
            if (!craft.loaded) return false; // Unloaded resource checking is unreliable.
            Dictionary<PartResource, double> toDraw = new Dictionary<PartResource,double>();
            double resRemaining = resAmount;
            foreach (Part cPart in craft.Parts)
            {
                foreach (PartResource cRes in cPart.Resources)
                {
                    if (cRes.resourceName != res) continue;
                    if (cRes.amount == 0) continue;
                    if (cRes.amount >= resRemaining)
                    {
                        toDraw.Add(cRes, resRemaining);
                        resRemaining = 0;
                    } else
                    {
                        toDraw.Add(cRes, cRes.amount);
                        resRemaining -= cRes.amount;
                    }
                }
                if (resRemaining <= 0) break;
            }
            if (resRemaining > 0) return false;
            if (consumeResource)
            {
                foreach (KeyValuePair<PartResource, double> drawSource in toDraw)
                {
                    drawSource.Key.amount -= drawSource.Value;
                }
            }
            return true;
        }

        // Startup sequence for beacon.
        [KSPEvent(name="BeaconInitialize", active = true, guiActive = true, guiName = "Initialize Beacon")]
        public void BeaconInitialize()
        {
            checkOwnTechBoxes();
            if (!requireResource(vessel, "Karborundum", 0.1, false))
            {
                ScreenMessages.PostScreenMessage("Cannot activate!  Insufficient Karborundum to initiate reaction.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude > gLimit) // Check our G forces.
            {
                print("Too deep in gravity well to activate!");
                string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                ScreenMessages.PostScreenMessage("Cannot activate!  Gravity from " + thevar + vessel.mainBody.name + " is too strong.",5.0f,ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (vessel.altitude < (vessel.mainBody.Radius * .25f)) // Check for radius limit.
            {
                string thevar = (vessel.mainBody.name == "Mun" || vessel.mainBody.name == "Sun") ? "the " : string.Empty;
                ScreenMessages.PostScreenMessage("Cannot activate!  Beacon is too close to " + thevar + vessel.mainBody.name + ".", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            if (!requireResource(vessel, "ElectricCharge", neededEC, true))
            {
                ScreenMessages.PostScreenMessage("Cannot activate!  Insufficient electric power to initiate reaction.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            ModuleAnimateGeneric MAG = part.FindModuleImplementing<ModuleAnimateGeneric>();
            print("Activating beacon!  Toggling MAG from " + MAG.status + "-" + MAG.Progress);
            print("EC Activation charge at " + neededEC + "(" + FlightGlobals.getGeeForceAtPosition(vessel.GetWorldPos3D()).magnitude + "/" + gLimit + ", " + fuelOnBoard + ")");
            MAG.Toggle();
            activated = true;
            part.force_activate();
            Fields["neededEC"].guiActive = false;
            Fields["constantEC"].guiActive = true;
            Events["BeaconInitialize"].active = false;
            Events["BeaconShutdown"].active = true;
            beaconStatus = "Active.";
        }

        [KSPEvent(name = "BeaconShutdown", active = false, guiActive = true, guiName = "Shutdown")]
        public void BeaconShutdown()
        {
            ModuleAnimateGeneric MAG = part.FindModuleImplementing<ModuleAnimateGeneric>();
            print("Deactivating beacon!  Toggling MAG from " + MAG.status + "-" + MAG.Progress);
            MAG.Toggle();
            beaconStatus = "Offline.";
            part.deactivate();
            activated = false;
            Fields["constantEC"].guiActive = false;
            Events["BeaconShutdown"].active = false;
            Events["BeaconInitialize"].active = true;
        }
    }
}
