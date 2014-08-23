using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ESLDCore
{
    public class ESLDHailer : PartModule
    {
        protected Rect BeaconWindow;
        protected Rect ConfirmWindow;
        public ESLDBeacon nearBeacon = null;
        public Vessel farBeacon = null;
        public string farBeaconModel = "";
        public Dictionary<Vessel, string> farTargets = new Dictionary<Vessel, string>();
        public bool isJumping = false;
        public double precision;
        public OrbitDriver hailerOrbit;
        public double lastRemDist;

        // GUI Open?
        [KSPField(guiName = "GUIOpen", isPersistant = true, guiActive = false)]
        public bool guiopen;

        [KSPField(guiName = "Beacon", guiActive = false)]
        public string hasNearBeacon;

        [KSPField(guiName = "Beacon Distance", guiActive = false, guiUnits = "m")]
        public double nearBeaconDistance;

        [KSPField(guiName = "Drift", guiActive = false, guiUnits = "m/s")]
        public double nearBeaconRelVel;

        // Calculate base cost in units of Karborundum before penalties for a transfer.
        private double getTripBaseCost(double tripdist, double tonnage, string nbModel)
        {
            double yardstick = Math.Sqrt(Math.Sqrt(13599840256));
            switch (nbModel)
            {
                case "LB10":
                    double distpenalty = 0;
                    if (tripdist > 1000000000) distpenalty = 2;
                    return ((Math.Pow(tonnage, 1 + (.001 * tonnage) + distpenalty) / 10) * ((Math.Sqrt(Math.Sqrt(tripdist * (tripdist / 5E6)))) / yardstick) / tonnage * 10000) * tonnage / 10000;
                case "LB15":
                    return (700 + (Math.Pow(tonnage, 1 + (.0002 * Math.Pow(tonnage, 2))) / 10) * ((Math.Sqrt(Math.Sqrt(tripdist * (tripdist / 5E10)))) / yardstick) / tonnage * 10000) * tonnage / 10000;
                case "LB100":
                    return (500 + (Math.Pow(tonnage, 1 + (.00025 * tonnage)) / 20) * ((Math.Sqrt(Math.Sqrt(Math.Sqrt(tripdist * 25000)))) / Math.Sqrt(yardstick)) / tonnage * 10000) * tonnage / 10000;
                case "IB1":
                    return ((((Math.Pow(tonnage, 1 + (tonnage / 6000)) * 0.9) / 10) * ((Math.Sqrt(Math.Sqrt(tripdist + 2E11))) / yardstick) / tonnage * 10000) * tonnage / 10000);
                default:
                    return 1000;
            }
        }

        // Calculate Jump Offset
        private Vector3d getJumpOffset(Vessel near, Vessel far)
        {
            Vector3d farRealVelocity = far.orbit.vel;
            CelestialBody farRefbody = far.mainBody;
            while (farRefbody.flightGlobalsIndex != 0) // Kerbol
            {
                farRealVelocity += farRefbody.orbit.vel;
                farRefbody = farRefbody.referenceBody;
            }
            Vector3d nearRealVelocity = near.orbit.vel;
            CelestialBody nearRefbody = near.mainBody;
            while (nearRefbody.flightGlobalsIndex != 0) // Kerbol
            {
                nearRealVelocity += nearRefbody.orbit.vel;
                nearRefbody = nearRefbody.referenceBody;
            }
            return nearRealVelocity - farRealVelocity;
        }

        // Calculate AMU cost in units of Karborundum given two vessel endpoints and the tonnage of the transferring vessel.
        private double getAMUCost(Vessel near, Vessel far, double tton)
        {
            Vector3d velDiff = getJumpOffset(near, far) - far.orbit.vel;
            double comp = velDiff.magnitude;
            return Math.Round(((comp * tton) / (comp + (tton * 25))) * 100) / 100;
        }

        // Find parts that need a HCU to transfer.
        private Dictionary<Part, string> getHCUParts(Vessel craft)
        {
            Array highEnergyResources = new string[7] { "karborundum", "uranium", "plutonium", "antimatter", "thorium", "nuclear", "exotic" };
            Dictionary<Part, string> HCUParts = new Dictionary<Part, string>();
            foreach (Part vpart in vessel.Parts)
            {
                foreach (PartResource vres in vpart.Resources)
                {
                    foreach (string checkr in highEnergyResources)
                    if (vres.resourceName.ToLower().Contains(checkr) && vres.amount > 0)
                    {
                        HCUParts.Add(vpart, vres.resourceName);
                    }
                }
            }
            return HCUParts;
        }

        // Calculate how far away from a beacon the ship will arrive.
        private double getTripSpread(double tripdist, string fbmodel)
        {
            double driftmodifier = 1;
            switch (fbmodel)
            {
                case "LB10":
                    driftmodifier = 10;
                    break;
                case "LB15":
                    driftmodifier = 50;
                    break;
                case "LB100":
                    driftmodifier = 80;
                    break;
                case "IB1":
                    driftmodifier = 2;
                    break;
                default:
                    driftmodifier = 1;
                    break;
            }
            return Math.Round(Math.Log(tripdist) / Math.Log(driftmodifier) * 10) * 100;
        }

        // Finds if the path between beacons passes too close to a planet or is within its gravity well.
        public KeyValuePair<string, CelestialBody> HasTransferPath(Vessel vOrigin, Vessel vDestination, double gLimit)
        {
            // Cribbed with love from RemoteTech.  I have no head for vectors.
            var returnPair = new KeyValuePair<string, CelestialBody>("start", vOrigin.mainBody);
            Vector3d opos = vOrigin.GetWorldPos3D();
            Vector3d dpos = vDestination.GetWorldPos3D();
            foreach (CelestialBody rock in FlightGlobals.Bodies)
            {
                Vector3d bodyFromOrigin = rock.position - opos;
                Vector3d destFromOrigin = dpos - opos;
                if (Vector3d.Dot(bodyFromOrigin, destFromOrigin) <= 0) continue;
                Vector3d destFromOriginNorm = destFromOrigin.normalized;
                if (Vector3d.Dot(bodyFromOrigin, destFromOriginNorm) >= destFromOrigin.magnitude) continue;
                Vector3d lateralOffset = bodyFromOrigin - Vector3d.Dot(bodyFromOrigin, destFromOriginNorm) * destFromOriginNorm;
                double limbo = Math.Sqrt((6.673E-11 * rock.Mass) / gLimit) - rock.Radius; // How low can we go?
                string limbotype = "Gravity";
                if (limbo < rock.Radius + rock.Radius * 0.25)
                {
                    limbo = rock.Radius + rock.Radius * .025;
                    limbotype = "Proximity";
                }
                if (lateralOffset.magnitude < limbo)
                {
                    returnPair = new KeyValuePair<string, CelestialBody>(limbotype, rock);
                    //print("Lateral Offset was " + lateralOffset.magnitude + "m and needed to be " + limbo + "m, failed due to " + limbotype + " check for " + rock.name + ".");
                    return returnPair;
                }
            }
            returnPair = new KeyValuePair<string, CelestialBody>("OK", null);
            return returnPair;
        }

        // Find loaded beacons.  Only in physics distance, since otherwise they're too far out.
        private ESLDBeacon ScanForNearBeacons()
        {
            hasNearBeacon = "Not Present";
            Fields["hasNearBeacon"].guiActive = true;
            foreach (ESLDBeacon selfBeacon in vessel.FindPartModulesImplementing<ESLDBeacon>())
            {
                if (selfBeacon.beaconModel == "IB1" && selfBeacon.activated == true)
                {
                    nearBeaconDistance = 0;
                    nearBeaconRelVel = 0;
                    Fields["nearBeaconDistance"].guiActive = false;
                    Fields["nearBeaconRelVel"].guiActive = false;
                    hasNearBeacon = "Onboard";
                    return selfBeacon;
                }
            }
            foreach (Vessel craft in FlightGlobals.Vessels)
            {
                if (craft.loaded == false) continue;                // Eliminate far away craft.
                if (craft == vessel) continue;                      // Eliminate current craft.
                if (craft == FlightGlobals.ActiveVessel) continue;
                if (craft.FindPartModulesImplementing<ESLDBeacon>().Count == 0) continue; // Has beacon?
                ESLDBeacon craftbeacon = craft.FindPartModulesImplementing<ESLDBeacon>().First(); // Should later implement some way of recognizing multiple beacons.
                if (craftbeacon.activated == false) { continue; }   // Beacon active?
                if (craftbeacon.beaconModel == "IB1") { continue; } // Jumpdrives can't do remote transfers.
                Fields["nearBeaconDistance"].guiActive = true;         // How far away is it?
                nearBeaconDistance = Math.Round(Vector3d.Distance(vessel.GetWorldPos3D(), craft.GetWorldPos3D()));
                Fields["nearBeaconRelVel"].guiActive = true;
                nearBeaconRelVel = Math.Round(Vector3d.Magnitude(vessel.obt_velocity - craft.obt_velocity) * 10) / 10;
                hasNearBeacon = "Present";
                return craftbeacon;
            }
            Fields["nearBeaconDistance"].guiActive = false;
            Fields["nearBeaconRelVel"].guiActive = false;
            nearBeacon = null;
            return null;
        }

        // Finds beacon targets.  Only starts polling when the GUI is open.
        public void listFarBeacons()
        {
            farTargets.Clear();
            foreach (Vessel craft in FlightGlobals.Vessels)
            {
                if (craft.loaded == true) continue;
                if (craft == vessel) continue;
                if (craft == FlightGlobals.ActiveVessel) continue;
                if (craft.situation != Vessel.Situations.ORBITING) continue;
                foreach (ProtoPartSnapshot ppart in craft.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartModuleSnapshot pmod in ppart.modules)
                    {
                        if (pmod.moduleName != "ESLDBeacon") continue;
                        if (pmod.moduleValues.GetValue("beaconStatus") != "Active.") continue;
                        farTargets.Add(craft, pmod.moduleValues.GetValue("beaconModel"));
                    }
                }
            }
        }

        public override void OnFixedUpdate()
        {
            var startState = hasNearBeacon;
            nearBeacon = ScanForNearBeacons();
            if (nearBeacon == null)
            {
                if (startState != hasNearBeacon)
                {
                    HailerGUIClose();
                }
                Events["HailerGUIClose"].active = false;
                Events["HailerGUIOpen"].active = false;
            }
            else
            {
                Events["HailerGUIClose"].active = false;
                Events["HailerGUIOpen"].active = true;
                if (guiopen) listFarBeacons();
            }
        }

        // Screen 1 of beacon interface, displays beacons and where they go along with some fuel calculations. 
        private void BeaconInterface(int GuiId)
        {
            GUIStyle buttonHasFuel = new GUIStyle(GUI.skin.button);
            buttonHasFuel.padding = new RectOffset(8, 8, 8, 8);
            buttonHasFuel.normal.textColor = buttonHasFuel.focused.textColor = Color.green;
            buttonHasFuel.hover.textColor = buttonHasFuel.active.textColor = Color.white;

            GUIStyle buttonNoFuel = new GUIStyle(GUI.skin.button);
            buttonNoFuel.padding = new RectOffset(8, 8, 8, 8);
            buttonNoFuel.normal.textColor = buttonNoFuel.focused.textColor = Color.red;
            buttonNoFuel.hover.textColor = buttonNoFuel.active.textColor = Color.yellow;

            GUIStyle buttonNoPath = new GUIStyle(GUI.skin.button);
            buttonNoPath.padding = new RectOffset(8, 8, 8, 8);
            buttonNoPath.normal.textColor = buttonNoFuel.focused.textColor = Color.gray;
            buttonNoPath.hover.textColor = buttonNoFuel.active.textColor = Color.gray;

            GUIStyle buttonNeutral = new GUIStyle(GUI.skin.button);
            buttonNeutral.padding = new RectOffset(8, 8, 8, 8);
            buttonNeutral.normal.textColor = buttonNoFuel.focused.textColor = Color.white;
            buttonNeutral.hover.textColor = buttonNoFuel.active.textColor = Color.white;

            GUILayout.BeginVertical(HighLogic.Skin.scrollView);
            if (farTargets.Count() < 1 || nearBeacon == null)
            {
                GUILayout.Label("No active beacons found.");
            }
            else
            {
                double tonnage = vessel.GetTotalMass();
                Vessel nbparent = nearBeacon.vessel;
                string nbModel = nearBeacon.beaconModel;
                nearBeacon.checkOwnTechBoxes();
                double nbfuel = nearBeacon.fuelOnBoard;
                double driftpenalty = Math.Pow(Math.Floor(nearBeaconDistance / 200), 2) + Math.Floor(Math.Pow(nearBeaconRelVel, 1.5));
                if (driftpenalty > 0) GUILayout.Label("+" + driftpenalty + "% due to Drift.");
                if (!nearBeacon.hasHCU)
                {
                    Dictionary<Part, string> HCUParts = getHCUParts(vessel);
                    if (vessel.GetCrewCount() > 0 || HCUParts.Count > 0) GUILayout.Label("WARNING: This beacon has no Heisenkerb Compensator.");
                    if (vessel.GetCrewCount() > 0) GUILayout.Label("Transfer will kill crew.");
                    if (HCUParts.Count > 0) GUILayout.Label("Some resources will destabilize.");
                }
                foreach (KeyValuePair<Vessel, string> ftarg in farTargets)
                {
                    double tripdist = Vector3d.Distance(nbparent.GetWorldPos3D(), ftarg.Key.GetWorldPos3D());
                    double tripcost = getTripBaseCost(tripdist, tonnage, nbModel);
                    if (nearBeacon.hasSCU && driftpenalty == 0) tripcost *= 0.9;
                    if (tripcost == 0) continue;
                    tripcost += tripcost * (driftpenalty * .01);
                    if (nearBeacon.hasAMU) tripcost += getAMUCost(vessel, ftarg.Key, tonnage);
                    tripcost = Math.Round(tripcost * 100) / 100;
                    string targetSOI = ftarg.Key.mainBody.name;
                    double targetAlt = Math.Round(ftarg.Key.altitude / 1000);
                    GUIStyle fuelstate = buttonNoFuel;
                    string blockReason = "";
                    string blockRock = "";
                    if (tripcost <= nbfuel) // Show blocked status only for otherwise doable transfers.
                    {
                        fuelstate = buttonHasFuel;
                        KeyValuePair<string, CelestialBody> checkpath = HasTransferPath(nbparent, ftarg.Key, nearBeacon.gLimit);
                        if (checkpath.Key != "OK")
                        {
                            fuelstate = buttonNoPath;
                            blockReason = checkpath.Key;
                            blockRock = checkpath.Value.name;
                        }
                    }
                    if (GUILayout.Button(ftarg.Value + " " + ftarg.Key.vesselName + "(" + targetSOI + ", " + targetAlt + "km) | " + tripcost, fuelstate))
                    {
                        if (fuelstate == buttonHasFuel)
                        {
                            farBeacon = ftarg.Key;
                            farBeaconModel = ftarg.Value;
                            drawConfirm();
                            RenderingManager.AddToPostDrawQueue(4, new Callback(drawConfirm));
                            HailerGUIClose();
                        }
                        else
                        {
                            string messageToPost = "Cannot Warp: Origin beacon has " + nbfuel + " of " + tripcost + " Karborundum required to warp.";
                            string thevar = "";
                            if (blockRock == "Mun" || blockRock == "Sun") thevar = "the ";
                            if (fuelstate == buttonNoPath && blockReason == "Gravity") messageToPost = "Cannot Warp: Path of transfer intersects a high-gravity area around " + thevar + blockRock + ".";
                            if (fuelstate == buttonNoPath && blockReason == "Proximity") messageToPost = "Cannot Warp: Path of transfer passes too close to " + thevar + blockRock + ".";
                            ScreenMessages.PostScreenMessage(messageToPost, 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }
            }
            if (GUILayout.Button("Close Beacon Interface", buttonNeutral))
            {
                HailerGUIClose();
            }
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

        }

        private void ConfirmInterface(int GuiID) // Second beacon interface window.  
        {

            GUIStyle buttonNeutral = new GUIStyle(GUI.skin.button);
            buttonNeutral.padding = new RectOffset(8, 8, 8, 8);
            buttonNeutral.normal.textColor = buttonNeutral.focused.textColor = Color.white;
            buttonNeutral.hover.textColor = buttonNeutral.active.textColor = Color.white;

            GUIStyle labelHasFuel = new GUIStyle(GUI.skin.label);
            labelHasFuel.normal.textColor = Color.green;

            GUIStyle labelNoFuel = new GUIStyle(GUI.skin.label);
            labelNoFuel.normal.textColor = Color.red;
            GUILayout.BeginVertical(HighLogic.Skin.scrollView);

            double tripdist = Vector3d.Distance(nearBeacon.vessel.GetWorldPos3D(), farBeacon.GetWorldPos3D());
            double tonnage = vessel.GetTotalMass();
            Vessel nbparent = nearBeacon.vessel;
            string nbModel = nearBeacon.beaconModel;
            double tripcost = getTripBaseCost(tripdist, tonnage, nbModel);
            double driftpenalty = Math.Pow(Math.Floor(nearBeaconDistance / 200), 2) + Math.Floor(Math.Pow(nearBeaconRelVel, 1.5));
            if (driftpenalty > 0) GUILayout.Label("+" + driftpenalty + "% due to Drift.");
            if (!nearBeacon.hasHCU)
            {
                Dictionary<Part, string> HCUParts = getHCUParts(vessel);
                if (vessel.GetCrewCount() > 0 || HCUParts.Count > 0) GUILayout.Label("WARNING: This beacon has no Heisenkerb Compensator.", labelNoFuel);
                if (vessel.GetCrewCount() > 0) GUILayout.Label("Transfer will kill crew.", labelNoFuel);
                if (HCUParts.Count > 0)
                {
                    GUILayout.Label("These resources will destabilize in transit:", labelNoFuel);
                    foreach (KeyValuePair<Part, string> hcuresource in HCUParts)
                    {
                        GUILayout.Label(hcuresource.Key.name + " - " + hcuresource.Value, labelNoFuel);
                    }
                }
            }
            GUILayout.Label("Confirm Warp:");
            var basecost = Math.Round(tripcost * 100) / 100;
            GUILayout.Label("Base Cost: " + basecost + " Karborundum.");
            if (nearBeacon.hasSCU && driftpenalty == 0)
            {
                GUILayout.Label("Superconducting Coil Array reduces cost by 10%.");
                tripcost *= 0.9;
            }
            if (driftpenalty > 0) GUILayout.Label("Relative speed and distance to beacon adds " + driftpenalty + "%.");
            tripcost += tripcost * (driftpenalty * .01);
            tripcost = Math.Round(tripcost * 100) / 100;
            if (nearBeacon.hasAMU)
            {
                double AMUCost = getAMUCost(vessel, farBeacon, tonnage);
                GUILayout.Label("AMU Compensation adds " + AMUCost + " Karborundum.");
                tripcost += AMUCost;
            }
            GUILayout.Label("Total Cost: " + tripcost + " Karborundum.");
            GUILayout.Label("Destination: " + farBeacon.mainBody.name + " at " + Math.Round(farBeacon.altitude / 1000) + "km.");
            precision = getTripSpread(tripdist, farBeaconModel);
            GUILayout.Label("Transfer will emerge within " + precision + "m of destination beacon.");
            if (!nearBeacon.hasAMU)
            {
                Vector3d transferVelOffset = getJumpOffset(vessel, farBeacon);
                GUILayout.Label("Velocity relative to exit beacon will be " + Math.Round(transferVelOffset.magnitude) + "m/s.");
            }
            double retTripCost = 0;
            double checkfuel = 0;
            bool fuelcheck = false;
            foreach (ProtoPartSnapshot ppart in farBeacon.protoVessel.protoPartSnapshots)
            {
                foreach (ProtoPartModuleSnapshot pmod in ppart.modules)
                {
                    if (pmod.moduleName == "ESLDBeacon" && pmod.moduleValues.GetValue("beaconStatus") == "Active.")
                    {
                        string pmodel = pmod.moduleValues.GetValue("beaconModel");
                        fuelcheck = double.TryParse(pmod.moduleValues.GetValue("fuelOnBoard"), out checkfuel);
                        if (retTripCost < getTripBaseCost(tripdist, tonnage, pmodel)) retTripCost = getTripBaseCost(tripdist, tonnage, pmodel);
                    }
                }
            }
            string fuelmessage = "Destination beacon's fuel could not be checked.";
            if (fuelcheck) fuelmessage = "Destination beacon has " + checkfuel + " Karborundum.";
            GUILayout.Label(fuelmessage);
            retTripCost = Math.Round(retTripCost * 100) / 100;
            if (retTripCost <= checkfuel)
            {
                GUILayout.Label("Destination beacon can make return trip using " + retTripCost + " Karborundum.", labelHasFuel);
            }
            else
            {
                GUILayout.Label("Destination beacon would need  " + retTripCost + " Karborundum for return trip using active beacons.", labelNoFuel);
            }
            if (GUILayout.Button("Confirm and Warp", buttonNeutral))
            {
                RenderingManager.RemoveFromPostDrawQueue(4, new Callback(drawConfirm));
                HailerGUIClose();
                // Deduct jump fuel, abort if not enough.
                var fuelBalance = tripcost;
                KeyValuePair<string, CelestialBody> checkpath = HasTransferPath(nbparent, farBeacon, nearBeacon.gLimit); // One more check for a clear path in case they left the window open too long.
                bool finalPathCheck = false;
                if (checkpath.Key == "OK")
                {
                    finalPathCheck = true;
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Transfer path has become obstructed, aborting jump.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                foreach (Part bpart in nbparent.Parts) // Take our pound of flesh.
                {
                    foreach (PartResource bres in bpart.Resources)
                    {
                        if (bres.resourceName == "Karborundum")
                        {
                            if (bres.amount < fuelBalance)
                            {
                                fuelBalance -= bres.amount;
                                bres.amount = 0;
                            }
                            else
                            {
                                bres.amount -= fuelBalance;
                                fuelBalance = 0;
                            }
                        }
                    }
                }
                if (fuelBalance == 0 && finalPathCheck) // Fuel is paid for and path is clear.
                {
                    // Buckle up!
                    if (!nearBeacon.hasHCU) // Penalize for HCU not being present/online.
                    {
                        List<ProtoCrewMember> crewList = new List<ProtoCrewMember>();
                        List<Part> crewParts = new List<Part>();
                        foreach (Part vpart in vessel.Parts)
                        {
                            foreach (ProtoCrewMember crew in vpart.protoModuleCrew)
                            {
                                crewParts.Add(vpart);
                                crewList.Add(crew);
                            }
                        }
                        for (int i = crewList.Count - 1; i >= 0; i--)
                        {
                            if (i >= crewList.Count)
                            {
                                if (crewList.Count == 0) break;
                                i = crewList.Count - 1;
                            }
                            ProtoCrewMember tempCrew = crewList[i];
                            crewList.RemoveAt(i);
                            ScreenMessages.PostScreenMessage(tempCrew.name + " was killed in transit!", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                            crewParts[i].RemoveCrewmember(tempCrew);
                            crewParts.RemoveAt(i);
                            tempCrew.Die();
                        }
                        Dictionary<Part, string> HCUParts = getHCUParts(vessel);
                        List<Part> HCUList = new List<Part>();
                        foreach (KeyValuePair<Part, string> HCUPart in HCUParts)
                        {
                            HCUList.Add(HCUPart.Key);
                        }
                        HCUParts.Clear();
                        for (int i = HCUList.Count - 1; i >= 0; i--)
                        {
                            if (i >= HCUList.Count)
                            {
                                if (HCUList.Count == 0) break;
                                i = HCUList.Count - 1;
                            }
                            Part tempPart = HCUList[i];
                            HCUList.RemoveAt(i);
                            tempPart.explosionPotential = 1;
                            tempPart.explode();
                            tempPart.Die();
                        }
                    }
                    Vector3d transferVelOffset = getJumpOffset(vessel, farBeacon);
                    if (nearBeacon.hasAMU) transferVelOffset = farBeacon.orbit.vel;
                    Vector3d spread = ((UnityEngine.Random.onUnitSphere + UnityEngine.Random.insideUnitSphere) / 2) * (float)precision;
                    vessel.Landed = false;
                    vessel.Splashed = false;
                    vessel.landedAt = string.Empty;
                    OrbitPhysicsManager.HoldVesselUnpack(180);
                    vessel.GoOnRails();
                    vessel.situation = Vessel.Situations.ORBITING;
                    vessel.orbit.UpdateFromStateVectors(farBeacon.orbit.pos + spread, transferVelOffset, farBeacon.mainBody, Planetarium.GetUniversalTime());
                    vessel.orbit.Init();
                    vessel.orbit.UpdateFromUT(Planetarium.GetUniversalTime());
                    vessel.orbitDriver.pos = vessel.orbit.pos.xzy;
                    vessel.orbitDriver.vel = vessel.orbit.vel;
                }
                else
                {
                    ScreenMessages.PostScreenMessage("Jump failed!  Origin beacon did not have enough fuel to execute transfer.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (GUILayout.Button("Back", buttonNeutral))
            {
                RenderingManager.RemoveFromPostDrawQueue(4, new Callback(drawConfirm));
                HailerGUIOpen();
            }
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void drawGUI()
        {
            BeaconWindow = GUILayout.Window(1, BeaconWindow, BeaconInterface, "Warp Information", GUILayout.MinWidth(400), GUILayout.MinHeight(200));
            if ((BeaconWindow.x == 0) && (BeaconWindow.y == 0))
            {
                BeaconWindow = new Rect(Screen.width / 2, Screen.height / 2, 10, 10);
            }
        }

        private void drawConfirm()
        {
            ConfirmWindow = GUILayout.Window(2, ConfirmWindow, ConfirmInterface, "Pre-Warp Confirmation", GUILayout.MinWidth(400), GUILayout.MinHeight(200));
            if ((ConfirmWindow.x == 0) && (ConfirmWindow.y == 0))
            {
                ConfirmWindow = new Rect(Screen.width / 2, Screen.height / 2, 10, 10);
            }
        }

        [KSPEvent(name = "HailerActivate", active = true, guiActive = true, guiName = "Initialize Hailer")]
        public void HailerActivate()
        {
            part.force_activate();
            Events["HailerActivate"].active = false;
            Events["HailerDeactivate"].active = true;
            ScanForNearBeacons();
            listFarBeacons();
        }
        [KSPEvent(name = "HailerGUIOpen", active = false, guiActive = true, guiName = "Beacon Interface")]
        public void HailerGUIOpen()
        {
            RenderingManager.AddToPostDrawQueue(3, new Callback(drawGUI));
            Events["HailerGUIOpen"].active = false;
            Events["HailerGUIClose"].active = true;
            guiopen = true;
        }
        [KSPEvent(name = "HailerGUIClose", active = false, guiActive = true, guiName = "Close Interface")]
        public void HailerGUIClose()
        {
            RenderingManager.RemoveFromPostDrawQueue(3, new Callback(drawGUI));
            Events["HailerGUIClose"].active = false;
            Events["HailerGUIOpen"].active = true;
            guiopen = false;
        }
        [KSPEvent(name = "HailerDeactivate", active = false, guiActive = true, guiName = "Shut Down Hailer")]
        public void HailerDeactivate()
        {
            part.deactivate();
            HailerGUIClose();
            RenderingManager.RemoveFromPostDrawQueue(4, new Callback(drawConfirm));
            Events["HailerDeactivate"].active = false;
            Events["HailerActivate"].active = true;
            Events["HailerGUIOpen"].active = false;
            Events["HailerGUIClose"].active = false;
            Fields["hasNearBeacon"].guiActive = false;
            Fields["nearBeaconDistance"].guiActive = false;
            Fields["nearBeaconRelVel"].guiActive = false;
        }

    }
}
