using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ScrollingText : MonoBehaviour
{
    public int maxLength = 20;
    public string message = "";
    public float speed = 0.2f;

    private int index;
    private Text textComponent;

    private void Start()
    {
        textComponent = GetComponent<Text>();
        StartCoroutine(ScrollText());
    }

    private IEnumerator ScrollText()
    {
        int direction = 1;

        while (true)
        {
            if (message.Length <= maxLength)
            {
                textComponent.text = message;
            }
            else
            {
                string scrollText = message.Substring(index, maxLength);
                textComponent.text = scrollText;
                yield return new WaitForSeconds(speed);

                if (index + maxLength >= message.Length)
                {
                    direction = -1;
                }
                else if (index <= 0)
                {
                    direction = 1;
                }

                index += direction;
            }
        }
    }

}