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
        public Dictionary<ESLDBeacon, string> nearBeacons = new Dictionary<ESLDBeacon, string>();
        public bool isJumping = false;
        public double precision;
        public OrbitDriver oPredictDriver = null;
        public OrbitRenderer oPredict = null;
        public Transform oOrigin = null;
        public LineRenderer oDirection = null;
        public GameObject oDirObj = null;
        public double lastRemDist;
        public bool wasInMapView;
        public bool nbWasUserSelected = false;
        public int currentBeaconIndex;
        public string currentBeaconDesc;
        public double HCUCost = 0;

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
                    return ((Math.Pow(tonnage, 1 + (.001 * tonnage) + distpenalty) / 10) * ((Math.Sqrt(Math.Sqrt(tripdist * (tripdist / 5E6)))) / yardstick) / tonnage * 10000) * tonnage / 2000;
                case "LB15":
                    return (700 + (Math.Pow(tonnage, 1 + (.0002 * Math.Pow(tonnage, 2))) / 10) * ((Math.Sqrt(Math.Sqrt(tripdist * (tripdist / 5E10)))) / yardstick) / tonnage * 10000) * tonnage / 2000;
                case "LB100":
                    return (500 + (Math.Pow(tonnage, 1 + (.00025 * tonnage)) / 20) * ((Math.Sqrt(Math.Sqrt(Math.Sqrt(tripdist * 25000)))) / Math.Sqrt(yardstick)) / tonnage * 10000) * tonnage / 2000;
                case "IB1":
                    return ((((Math.Pow(tonnage, 1 + (tonnage / 6000)) * 0.9) / 10) * ((Math.Sqrt(Math.Sqrt(tripdist + 2E11))) / yardstick) / tonnage * 10000) * tonnage / 2000);
                default:
                    return 1000;
            }
        }

        // Calculate Jump Offset
        private Vector3d getJumpOffset(Vessel near, Vessel far, string model)
        {
            Vector3d farRealVelocity = far.orbit.vel;
            CelestialBody farRefbody = far.mainBody;
            while (farRefbody.flightGlobalsIndex != 0) // Kerbol
            {
                farRealVelocity += farRefbody.orbit.vel;
                farRefbody = farRefbody.referenceBody;
            }
            if (model == "LB10") farRealVelocity -= far.orbit.vel; // LB10s don't respect destination velocity.
            Vector3d nearRealVelocity = near.orbit.vel;
            CelestialBody nearRefbody = near.mainBody;
            while (nearRefbody.flightGlobalsIndex != 0) // Kerbol
            {
                nearRealVelocity += nearRefbody.orbit.vel;
                nearRefbody = nearRefbody.referenceBody;
            }
            return nearRealVelocity - farRealVelocity;
        }

        // Mapview Utility
        private MapObject findVesselBody(Vessel craft)
        {
            int cInst = craft.mainBody.GetInstanceID();
//          foreach (MapObject mobj in MapView.FindObjectsOfType<MapObject>())
            foreach (MapObject mobj in MapView.MapCamera.targets)
            {
                if (mobj.celestialBody == null) continue;
                if (mobj.celestialBody.GetInstanceID() == cInst)
                {
                    return mobj;
                }
            }
            return null;
        }

        // Show exit orbital predictions
        private void showExitOrbit(Vessel near, Vessel far, string model)
        {
            // Recenter map, save previous state.
            wasInMapView = MapView.MapIsEnabled;
            if (!MapView.MapIsEnabled) MapView.EnterMapView();
            print("Finding target.");
            MapObject farTarget = findVesselBody(far);
            if (farTarget != null) MapView.MapCamera.SetTarget(farTarget);

            // Initialize projection stuff.
            print("Beginning orbital projection.");
            Vector3d exitTraj = getJumpOffset(near, far, model);
            oPredictDriver = new OrbitDriver();
            oPredictDriver.orbit = new Orbit();
            oPredictDriver.orbit.referenceBody = far.mainBody;
            oPredictDriver.referenceBody = far.mainBody;
            oPredictDriver.upperCamVsSmaRatio = 999999;  // Took forever to figure this out - this sets at what zoom level the orbit appears.  Was causing it not to appear at small bodies.
            oPredictDriver.lowerCamVsSmaRatio = 0.0001f;
            oPredictDriver.orbit.UpdateFromStateVectors(far.orbit.pos, exitTraj, far.mainBody, Planetarium.GetUniversalTime());
            oPredictDriver.orbit.Init();
            Vector3d p = oPredictDriver.orbit.getRelativePositionAtUT(Planetarium.GetUniversalTime());
            Vector3d v = oPredictDriver.orbit.getOrbitalVelocityAtUT(Planetarium.GetUniversalTime());
            oPredictDriver.orbit.h = Vector3d.Cross(p, v);
            oPredict = MapView.MapCamera.gameObject.AddComponent<OrbitRenderer>();
            oPredict.upperCamVsSmaRatio = 999999;
            oPredict.lowerCamVsSmaRatio = 0.0001f;
            oPredict.celestialBody = far.mainBody;
            oPredict.driver = oPredictDriver;
            oPredictDriver.Renderer = oPredict;
            
            // Splash some color on it.
            print("Displaying orbital projection.");
            oPredict.driver.drawOrbit = true;
            oPredict.driver.orbitColor = Color.red;
            oPredict.orbitColor = Color.red;
            oPredict.drawIcons = OrbitRenderer.DrawIcons.OBJ_PE_AP;
            oPredict.drawMode = OrbitRenderer.DrawMode.REDRAW_AND_RECALCULATE;

            // Directional indicator.
            oDirObj = new GameObject("Indicator");
            oDirObj.layer = 10; // Map layer!
            oDirection = oDirObj.AddComponent<LineRenderer>();
            oDirection.useWorldSpace = false;
            oOrigin = null;
            foreach (Transform sstr in ScaledSpace.Instance.scaledSpaceTransforms)
            {
                if (sstr.name == far.mainBody.name)
                {
                    oOrigin = sstr;
                    print("Found origin: " + sstr.name);
                    break;
                }
            }
            oDirection.transform.parent = oOrigin;
            oDirection.transform.position = ScaledSpace.LocalToScaledSpace(far.transform.position);
            oDirection.material = new Material(Shader.Find("Particles/Additive"));
            oDirection.SetColors(Color.clear, Color.red);
            oDirection.SetWidth(20.0f, 0.01f);
            oDirection.SetVertexCount(2);
            oDirection.SetPosition(0, Vector3d.zero + exitTraj.xzy.normalized * 10);
            oDirection.SetPosition(1, exitTraj.xzy.normalized * 50);
            oDirection.enabled = true;
            // */
        }

        // Update said predictions
        private void updateExitOrbit(Vessel near, Vessel far, string model)
        {
            Vector3d exitTraj = getJumpOffset(near, far, model);
            oPredict.driver.referenceBody = far.mainBody;
            oPredict.driver.orbit.referenceBody = far.mainBody;
            oPredict.driver.pos = far.orbit.pos;
            oPredict.celestialBody = far.mainBody;
            oPredictDriver.orbit.UpdateFromStateVectors(far.orbit.pos, exitTraj, far.mainBody, Planetarium.GetUniversalTime());

            oDirection.transform.position = ScaledSpace.LocalToScaledSpace(far.transform.position);
            oDirection.SetWidth(20.0f, 0.01f);
            oDirection.SetPosition(0, Vector3d.zero + exitTraj.xzy.normalized * 10);
            oDirection.SetPosition(1, exitTraj.xzy.normalized * 50);
            oDirection.transform.eulerAngles = Vector3d.zero;
        }

        // Back out of orbital predictions.
        private void hideExitOrbit(OrbitRenderer showOrbit)
        {
            showOrbit.drawMode = OrbitRenderer.DrawMode.OFF;
            showOrbit.driver.drawOrbit = false;
            showOrbit.drawIcons = OrbitRenderer.DrawIcons.NONE;

            oDirection.enabled = false;

            foreach (MapObject mobj in MapView.MapCamera.targets)
            {
                if (mobj.vessel == null) continue;
                if (mobj.vessel.GetInstanceID() == FlightGlobals.ActiveVessel.GetInstanceID())
                {
                    MapView.MapCamera.SetTarget(mobj);
                }
            }
            if (MapView.MapIsEnabled && !wasInMapView) MapView.ExitMapView();
        }

        // Calculate AMU cost in units of Karborundum given two vessel endpoints and the tonnage of the transferring vessel.
        private double getAMUCost(Vessel near, Vessel far, double tton, string model)
        {
            Vector3d velDiff = getJumpOffset(near, far, model) - far.orbit.vel;
            double comp = velDiff.magnitude;
            return Math.Round(((comp * tton) / Math.Pow(Math.Log10(comp * tton),2)) / 2) / 100;
        }

        // Find parts that need a HCU to transfer.
        private Dictionary<Part, string> getHCUParts(Vessel craft)
        {
            HCUCost = 0;
            Array highEnergyResources = new string[7] { "karborundum", "uranium", "plutonium", "antimatter", "thorium", "nuclear", "exotic" };
            Dictionary<Part, string> HCUParts = new Dictionary<Part, string>();
            foreach (Part vpart in vessel.Parts)
            {
                foreach (PartResource vres in vpart.Resources)
                {
                    foreach (string checkr in highEnergyResources)
                    if (vres.resourceName.ToLower().Contains(checkr) && vres.amount > 0)
                    {
                        if (HCUParts.Keys.Contains<Part>(vpart)) continue;
                        HCUCost += (vres.info.density * vres.amount * .02) / 0.02256;
                        HCUParts.Add(vpart, vres.resourceName);
                    }
                }
            }
            HCUCost += craft.GetCrewCount() * 0.9 / 1.13;
            HCUCost = Math.Round(HCUCost * 100) / 100;
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
            if (FlightGlobals.getGeeForceAtPosition(vDestination.GetWorldPos3D()).magnitude > gLimit) return new KeyValuePair<string, CelestialBody>("Gravity", vDestination.mainBody);
            returnPair = new KeyValuePair<string, CelestialBody>("OK", null);
            return returnPair;
        }

        // Find loaded beacons.  Only in physics distance, since otherwise they're too far out.
        private ESLDBeacon ScanForNearBeacons()
        {
            nearBeacons.Clear();
            Fields["hasNearBeacon"].guiActive = true;
            ESLDBeacon nearBeaconCandidate = null;
            int candidateIndex = 0;
            string candidateDesc = "";
            foreach (ESLDBeacon selfBeacon in vessel.FindPartModulesImplementing<ESLDBeacon>())
            {
                if (selfBeacon.beaconModel == "IB1" && selfBeacon.activated)
                {
                    nearBeaconDistance = 0;
                    nearBeaconRelVel = 0;
                    Fields["nearBeaconDistance"].guiActive = false;
                    Fields["nearBeaconRelVel"].guiActive = false;
                    hasNearBeacon = "Onboard";
                    return selfBeacon;
                }
            }
            double closest = 3000;
            foreach (Vessel craft in FlightGlobals.Vessels)
            {
                if (!craft.loaded) continue;                // Eliminate far away craft.
                if (craft == vessel) continue;                      // Eliminate current craft.
                if (craft == FlightGlobals.ActiveVessel) continue;
                if (craft.FindPartModulesImplementing<ESLDBeacon>().Count == 0) continue; // Has beacon?
                foreach (ESLDBeacon craftbeacon in craft.FindPartModulesImplementing<ESLDBeacon>())
                {
                    if (!craftbeacon.activated) { continue; }   // Beacon active?
                    if (craftbeacon.beaconModel == "IB1") { continue; } // Jumpdrives can't do remote transfers.
                    string bIdentifier = craftbeacon.beaconModel + " (" + craft.vesselName + ")";
                    nearBeacons.Add(craftbeacon, bIdentifier);
                    int nbIndex = nearBeacons.Count - 1;
                    nearBeaconDistance = Math.Round(Vector3d.Distance(vessel.GetWorldPos3D(), craft.GetWorldPos3D()));
                    if (closest > nearBeaconDistance)
                    {
                        nearBeaconCandidate = craftbeacon;
                        candidateIndex = nbIndex;
                        candidateDesc = bIdentifier;
                        closest = nearBeaconDistance;
                    }
                }
            }
            if (nearBeacon != null && nearBeacon.vessel.loaded && nbWasUserSelected && nearBeacon.activated) // If we've already got one, just update the display.
            {
                nearBeaconDistance = Math.Round(Vector3d.Distance(vessel.GetWorldPos3D(), nearBeacon.vessel.GetWorldPos3D()));
                nearBeaconRelVel = Math.Round(Vector3d.Magnitude(vessel.obt_velocity - nearBeacon.vessel.obt_velocity) * 10) / 10;
                return nearBeacon;
            }
            if (nearBeacons.Count > 0) // If we hadn't selected one previously return the closest one.
            {
                nbWasUserSelected = false;
                Vessel craft = nearBeaconCandidate.vessel;
                Fields["nearBeaconDistance"].guiActive = true;
                nearBeaconDistance = Math.Round(Vector3d.Distance(vessel.GetWorldPos3D(), craft.GetWorldPos3D()));
                Fields["nearBeaconRelVel"].guiActive = true;
                nearBeaconRelVel = Math.Round(Vector3d.Magnitude(vessel.obt_velocity - craft.obt_velocity) * 10) / 10;
                hasNearBeacon = "Present";
                currentBeaconIndex = candidateIndex;
                currentBeaconDesc = candidateDesc;
                return nearBeaconCandidate;
            }
            hasNearBeacon = "Not Present";
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
            if (!vessel.isActiveVessel) HailerGUIClose();
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
                Dictionary<Part, string> HCUParts = getHCUParts(vessel);
                if (!nearBeacon.hasHCU)
                {
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
                    if (nearBeacon.hasAMU) tripcost += getAMUCost(vessel, ftarg.Key, tonnage, nearBeacon.beaconModel);
                    double adjHCUCost = HCUCost;
                    if (nearBeacon.beaconModel == "IB1") adjHCUCost = Math.Round((HCUCost - (tripcost * 0.02)) * 100) / 100;
                    if (nearBeacon.hasHCU) tripcost += adjHCUCost;
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
                            if (!nearBeacon.hasAMU) showExitOrbit(vessel, farBeacon, nearBeacon.beaconModel);
                            RenderingManager.AddToPostDrawQueue(4, new Callback(drawConfirm));
                            HailerGUIClose();
                        }
                        else
                        {
                            print("Current beacon has a g limit of " + nearBeacon.gLimit);
                            string messageToPost = "Cannot Warp: Origin beacon has " + nbfuel + " of " + tripcost + " Karborundum required to warp.";
                            string thevar = (blockRock == "Mun" || blockRock == "Sun") ? "the " : string.Empty;
                            if (fuelstate == buttonNoPath && blockReason == "Gravity") messageToPost = "Cannot Warp: Path of transfer intersects a high-gravity area around " + thevar + blockRock + ".";
                            if (fuelstate == buttonNoPath && blockReason == "Proximity") messageToPost = "Cannot Warp: Path of transfer passes too close to " + thevar + blockRock + ".";
                            ScreenMessages.PostScreenMessage(messageToPost, 5.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                    }
                }
            }
            if(nearBeacons.Count > 1)
            {
                GUILayout.Label("Current Beacon: " + currentBeaconDesc);
                if (currentBeaconIndex >= nearBeacons.Count) currentBeaconIndex = nearBeacons.Count - 1;
                int nextIndex = currentBeaconIndex + 1;
                if (nextIndex >= nearBeacons.Count) nextIndex = 0;
                if (GUILayout.Button("Next Beacon (" + (currentBeaconIndex + 1) + " of " + nearBeacons.Count + ")", buttonNeutral))
                {
                    nbWasUserSelected = true;
                    nearBeacon = nearBeacons.ElementAt(nextIndex).Key;
                    currentBeaconDesc = nearBeacons.ElementAt(nextIndex).Value;
                    currentBeaconIndex = nextIndex;
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

            if (nearBeacon != null)
            {
                double tripdist = Vector3d.Distance(nearBeacon.vessel.GetWorldPos3D(), farBeacon.GetWorldPos3D());
                double tonnage = vessel.GetTotalMass();
                Vessel nbparent = nearBeacon.vessel;
                string nbModel = nearBeacon.beaconModel;
                double tripcost = getTripBaseCost(tripdist, tonnage, nbModel);
                double driftpenalty = Math.Pow(Math.Floor(nearBeaconDistance / 200), 2) + Math.Floor(Math.Pow(nearBeaconRelVel, 1.5));
                if (driftpenalty > 0) GUILayout.Label("+" + driftpenalty + "% due to Drift.");
                Dictionary<Part, string> HCUParts = getHCUParts(vessel);
                if (!nearBeacon.hasHCU)
                {
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
                    double AMUCost = getAMUCost(vessel, farBeacon, tonnage, nearBeacon.beaconModel);
                    GUILayout.Label("AMU Compensation adds " + AMUCost + " Karborundum.");
                    tripcost += AMUCost;
                }
                if (nearBeacon.hasHCU)
                {
                    double adjHCUCost = HCUCost;
                    if (nearBeacon.beaconModel == "IB1") adjHCUCost = Math.Round((HCUCost - (tripcost * 0.02)) * 100) / 100;
                    GUILayout.Label("HCU Shielding adds " + adjHCUCost + " Karborundum.");
                    tripcost += adjHCUCost;
                }
                GUILayout.Label("Total Cost: " + tripcost + " Karborundum.");
                GUILayout.Label("Destination: " + farBeacon.mainBody.name + " at " + Math.Round(farBeacon.altitude / 1000) + "km.");
                precision = getTripSpread(tripdist, farBeaconModel);
                GUILayout.Label("Transfer will emerge within " + precision + "m of destination beacon.");
                if (!nearBeacon.hasAMU)
                {
                    Vector3d transferVelOffset = getJumpOffset(vessel, farBeacon, nearBeacon.beaconModel);
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
                    GUILayout.Label("Destination beacon can make return trip using " + retTripCost + " (base cost) Karborundum.", labelHasFuel);
                }
                else
                {
                    GUILayout.Label("Destination beacon would need  " + retTripCost + " (base cost) Karborundum for return trip using active beacons.", labelNoFuel);
                }
                if (oPredict != null) updateExitOrbit(vessel, farBeacon, nearBeacon.beaconModel);
                if (GUILayout.Button("Confirm and Warp", buttonNeutral))
                {
                    RenderingManager.RemoveFromPostDrawQueue(4, new Callback(drawConfirm));
                    HailerGUIClose();
                    if (oPredict != null) hideExitOrbit(oPredict);
                    // Check transfer path one last time.
                    KeyValuePair<string, CelestialBody> checkpath = HasTransferPath(nbparent, farBeacon, nearBeacon.gLimit); // One more check for a clear path in case they left the window open too long.
                    bool finalPathCheck = false;
                    if (checkpath.Key == "OK") finalPathCheck = true;
                    // Check fuel one last time.
                    fuelcheck = false;
                    fuelcheck = nearBeacon.requireResource(nbparent, "Karborundum", tripcost, true);
                    if (fuelcheck && finalPathCheck) // Fuel is paid for and path is clear.
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
                            HCUParts = getHCUParts(vessel);
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
                        Vector3d transferVelOffset = getJumpOffset(vessel, farBeacon, nearBeacon.beaconModel);
                        if (nearBeacon.hasAMU) transferVelOffset = farBeacon.orbit.vel;
                        Vector3d spread = ((UnityEngine.Random.onUnitSphere + UnityEngine.Random.insideUnitSphere) / 2) * (float)precision;
                        vessel.Landed = false;
                        vessel.Splashed = false;
                        vessel.landedAt = string.Empty;
                        OrbitPhysicsManager.HoldVesselUnpack(180);
                        nbparent.GoOnRails();
                        vessel.GoOnRails();
                        vessel.situation = Vessel.Situations.ORBITING;
                        vessel.orbit.UpdateFromStateVectors(farBeacon.orbit.pos + spread, transferVelOffset, farBeacon.mainBody, Planetarium.GetUniversalTime());
                        vessel.orbit.Init();
                        vessel.orbit.UpdateFromUT(Planetarium.GetUniversalTime());
                        vessel.orbitDriver.pos = vessel.orbit.pos.xzy;
                        vessel.orbitDriver.vel = vessel.orbit.vel;
                    }
                    else if (!fuelcheck && finalPathCheck)
                    {
                        ScreenMessages.PostScreenMessage("Jump failed!  Origin beacon did not have enough fuel to execute transfer.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    else if (!finalPathCheck)
                    {
                        ScreenMessages.PostScreenMessage("Jump Failed!  Transfer path has become obstructed.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    else
                    {
                        ScreenMessages.PostScreenMessage("Jump Failed!  Origin beacon cannot complete transfer.", 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                }
            }
            else
            {
                if (oPredict != null) hideExitOrbit(oPredict);
            }
            if (!vessel.isActiveVessel)
            {
                RenderingManager.RemoveFromPostDrawQueue(4, new Callback(drawConfirm));
                if (oPredict != null) hideExitOrbit(oPredict);
            }
            if (GUILayout.Button("Back", buttonNeutral))
            {
                RenderingManager.RemoveFromPostDrawQueue(4, new Callback(drawConfirm));
                HailerGUIOpen();
                if (oPredict != null) hideExitOrbit(oPredict);
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
//            if (oPredict != null) hideExitOrbit(oPredict);
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
