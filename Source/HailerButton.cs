using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ESLDCore
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class HailerButton : MonoBehaviour
    {
        private ApplicationLauncherButton button;
        private Vessel vessel;
        private ESLDHailer hailer;
        private bool canHail = false;
        private Texture2D ESLDButtonOn = new Texture2D(38, 38, TextureFormat.ARGB32, false);
        public FlightCamera mainCam = null;
        public bool isDazzling = false;
        private float currentFOV = 60;
        private float userFOV = 60;
        private float currentDistance = 1;
        private float userDistance = 1;

        public void Update()
        {
            if(FlightGlobals.ActiveVessel != null) // Grab active vessel.
            {
                vessel = FlightGlobals.ActiveVessel;
                if (vessel.FindPartModulesImplementing<ESLDHailer>().Count == 0) // Has a hailer?
                {
                    canHail = false;
                    hailer = null;
                }
                else
                {
                    canHail = true;
                    hailer = vessel.FindPartModulesImplementing<ESLDHailer>().First();
                    foreach (ESLDHailer ehail in vessel.FindPartModulesImplementing<ESLDHailer>())
                    {
                        ehail.masterClass = this;
                    }
                }
            }
            if (canHail && this.button == null)
            {
                onGUIApplicationLauncherReady();
            }
            if (!canHail && this.button != null)
            {
                killButton();
            }
            // Sync GUI & Button States
            if (this.button != null)
            {
                if (this.button.State == RUIToggleButton.ButtonState.TRUE && !hailer.guiopen)
                {
                    this.button.SetFalse();
                }
                if (this.button.State == RUIToggleButton.ButtonState.FALSE && hailer.guiopen)
                {
                    this.button.SetTrue();
                }
            }
        }

        public void FixedUpdate()
        {
            if (isDazzling)
            {
                currentFOV = Mathf.Lerp(currentFOV, userFOV, 0.04f);
                currentDistance = Mathf.Lerp(currentDistance, userDistance, 0.04f);
                mainCam.SetFoV(currentFOV);
                mainCam.SetDistance(currentDistance);
                print("Distance: " + currentDistance);
                if (userFOV + 0.25 >= currentFOV)
                {
                    mainCam.SetFoV(userFOV);
                    mainCam.SetDistance(userDistance);
                    print("Done messing with camera!");
                    isDazzling = false;
                }
                
            }
        }

        public void Awake()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(onGUIApplicationLauncherReady);
            GameEvents.onGameSceneLoadRequested.Add(onSceneChangeRequest);
            GameEvents.onVesselChange.Add(onVesselChange);
            ESLDButtonOn = GameDatabase.Instance.GetTexture("ESLDBeacons/Textures/launcher", false);
            mainCam = FlightCamera.fetch;
        }

        public void onDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(onGUIApplicationLauncherReady);
            GameEvents.onGameSceneLoadRequested.Remove(onSceneChangeRequest);
            GameEvents.onVesselChange.Remove(onVesselChange);
            killButton();
        }

        private void onTrue()
        {
            if (hailer != null)
            {
                hailer.guiopen = true;
                hailer.HailerActivate();
                hailer.HailerGUIOpen();
            }
        }

        private void onFalse()
        {
            if (hailer != null)
            {
                hailer.HailerDeactivate();
            }
        }


        private void onGUIApplicationLauncherReady()
        {
            if (this.button != null)
            {
                killButton();
            }
            if (canHail)
            {
                this.button = ApplicationLauncher.Instance.AddModApplication(
                    this.onTrue,
                    this.onFalse,
                    null,
                    null,
                    null,
                    null,
                    ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                    ESLDButtonOn);
            }
        }

        public void onSceneChangeRequest(GameScenes _scene)
        {
            killButton();
        }

        public void onVesselChange(Vessel _vessel)
        {
            killButton();
        }

        private void killButton()
        {
            if (button != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(button);
            }
        }

        // Warp Effect
        public void dazzle()
        {
            userFOV = mainCam.FieldOfView;
            userDistance = mainCam.Distance;
            currentFOV = 180;
            currentDistance = 0.1f;
            isDazzling = true;
            print("Messing with camera!");
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
    }
        
}
