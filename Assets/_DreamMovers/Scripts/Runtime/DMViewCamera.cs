using Unity.Netcode;
using UnityEngine;

namespace DreamMovers
{
    /// <summary>
    /// Puts the scene camera where this player looks from: the own doll's eyes, or, while caught, behind the cotton
    /// ball teammates are carrying (UE5: the ball's spring arm camera). Sits on the main camera.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class DMViewCamera : MonoBehaviour
    {
        [SerializeField] float ballArmLength = 3.2f;
        [SerializeField] float ballPitch = 35f;

        Camera view;

        public static DMViewCamera Instance { get; private set; }
        public Camera Camera => view;
        /// <summary>The cotton ball this camera follows (this player is caught), or null.</summary>
        public DMCottonBall FollowedBall { get; private set; }

        void Awake()
        {
            Instance = this;
            view = GetComponent<Camera>();
            view.nearClipPlane = 0.03f;
            view.fieldOfView = Camera.HorizontalToVerticalFieldOfView(90f, Mathf.Max(0.1f, view.aspect));
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void LateUpdate()
        {
            view.fieldOfView = Camera.HorizontalToVerticalFieldOfView(90f, Mathf.Max(0.1f, view.aspect));
            FollowedBall = null;
            DMDoll doll = DMDoll.Local;
            if (doll != null)
            {
                transform.SetPositionAndRotation(doll.EyePosition, doll.ViewRotation);
                return;
            }
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
            {
                return;
            }
            DMCottonBall ball = DMCottonBall.ForClient(manager.LocalClientId);
            if (ball == null)
            {
                return;
            }
            FollowedBall = ball;
            // Fixed rotation, so the view does not tumble with the rolling ball; pulled in front of walls.
            Quaternion rotation = Quaternion.Euler(ballPitch, ball.FacingYaw, 0f);
            Vector3 pivot = ball.transform.position;
            Vector3 wanted = pivot - rotation * Vector3.forward * ballArmLength;
            if (DMPhysics.Linecast(pivot, wanted, ball.transform, out RaycastHit hit))
            {
                wanted = hit.point + hit.normal * 0.1f;
            }
            transform.SetPositionAndRotation(wanted, rotation);
        }
    }
}
