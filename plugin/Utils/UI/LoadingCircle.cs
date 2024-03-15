using UnityEngine;

namespace OdinOnDemand.Utils.UI
{
    public class LoadingCircle : MonoBehaviour
    {
        private RectTransform rectComponent;
        private readonly float rotateSpeed = 200f;

        private void Start()
        {
            rectComponent = GetComponent<RectTransform>();
        }

        private void Update()
        {
            if (isActiveAndEnabled) rectComponent.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);
        }
    }
}