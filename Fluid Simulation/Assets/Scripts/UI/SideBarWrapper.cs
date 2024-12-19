using System.Collections;
using System.Collections.Generic;
using Microsoft.Unity.VisualStudio.Editor;
using UnityEngine;
using UnityEngine.UI;

public class SideBarWrapper : MonoBehaviour
{
    [Header("This script handles the references and function calls for each of the sidebar buttons.\n This reduces the amount of drag and drops needed for managing the UI.\n")]

    [SerializeField] PauseMenuManager pauseMenuManager;
    [SerializeField] GameObject simSettingsPanel;
    [SerializeField] Simulation2D simulation2DScript;
    [SerializeField] GameObject informationPanel;

    [SerializeField] AudioSource audioSource;

    [SerializeField] UnityEngine.UI.Image PlayPauseSidebarIcon;
    [SerializeField] UnityEngine.UI.Image PlayPauseSidebarBG;
    [SerializeField] Sprite PauseIconImage;
    [SerializeField] Sprite PlayIconImage;

    public void PauseGame()
    {
        pauseMenuManager.PauseGame();
    }
    public void ShowSimulationSettings(){
        simSettingsPanel.SetActive(true);
        audioSource.Play();
    }
    public void TogglePauseFluidSimulation(){
        simulation2DScript.togglePause();
        audioSource.Play();

        UpdatePauseIcon();
    }
    public void stepFluidSimulation(){
        simulation2DScript.stepSimulation();
        audioSource.Play();

        PlayPauseSidebarIcon.sprite = PlayIconImage;
        PlayPauseSidebarBG.color = new Color(0.7058824f, 0.624576f, 0.1215686f);
    }
    public void resetFluidSimulation(){
        simulation2DScript.resetSimulation();
        audioSource.Play();
        UpdatePauseIcon();
    }
    public void ShowInformationPanel(){
        informationPanel.SetActive(true);
        audioSource.Play();
    }

    public void UpdatePauseIcon(){
        if(simulation2DScript.getPaused()){
            PlayPauseSidebarIcon.sprite = PlayIconImage;
            PlayPauseSidebarBG.color = new Color(0.7058824f, 0.624576f, 0.1215686f);
        } else{
            PlayPauseSidebarIcon.sprite = PauseIconImage;
            PlayPauseSidebarBG.color = new Color(0, 0, 0, 255);
        }
    }
}