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

        public void Awake()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(onGUIApplicationLauncherReady);
            GameEvents.onGameSceneLoadRequested.Add(onSceneChangeRequest);
            GameEvents.onVesselChange.Add(onVesselChange);
            ESLDButtonOn = GameDatabase.Instance.GetTexture("ESLDBeacons/Textures/launcher", false);
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
    }
        
}
