using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Synchronizes scene physics if not the default physics scene.
/// </summary>
public class PhysicsSceneSync : NetworkBehaviour
{
    /// <summary>
    /// True to synchronize physics 2d.
    /// </summary>
    [SerializeField]
    private bool _physics2D = true;
    /// <summary>
    /// True to synchronize physics 3d.
    /// </summary>
    [SerializeField]
    private bool _physics = true;
    /// <summary>
    /// Scenes which have physics handled by this script.
    /// </summary>
    private static HashSet<int> _synchronizedScenes = new HashSet<int>();

    public override void OnStartNetwork()
    {
        /* If scene is already synchronized do not take action.
         * This means the script was added twice to the same scene. */
        int sceneHandle = gameObject.scene.handle;
        if (_synchronizedScenes.Contains(sceneHandle))
            return;

        /* Set to synchronize the scene if either 2d or 3d
         * physics scene differ from the defaults. */
        _physics = (gameObject.scene.GetPhysicsScene() != Physics.defaultPhysicsScene);
        _physics2D = (gameObject.scene.GetPhysicsScene2D() != Physics2D.defaultPhysicsScene);

        /* If to synchronize 2d or 3d manually then
         * register to pre physics simulation. */
        if (_physics || _physics2D)
        {
            Debug.LogError($"Ok");
            _synchronizedScenes.Add(sceneHandle);
            base.TimeManager.OnPrePhysicsSimulation += TimeManager_OnPrePhysicsSimulation;
        }
    }

    public override void OnStopNetwork()
    {
        //Check to unsubscribe.
        if (_physics || _physics2D)
        {
            _synchronizedScenes.Remove(gameObject.scene.handle);
            base.TimeManager.OnPrePhysicsSimulation -= TimeManager_OnPrePhysicsSimulation;
        }
    }

    private void TimeManager_OnPrePhysicsSimulation(float delta)
    {
        /* If to simulate physics then do so on this objects
         * physics scene. If you know the object is not going to change
         * scenes you can cache the physics scenes
         * rather than look them up each time. */
        if (_physics)
            gameObject.scene.GetPhysicsScene().Simulate(delta);
        if (_physics2D)
            gameObject.scene.GetPhysicsScene2D().Simulate(delta);
    }

}