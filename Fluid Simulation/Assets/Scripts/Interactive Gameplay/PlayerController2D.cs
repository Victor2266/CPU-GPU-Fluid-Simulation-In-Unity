using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController2D : MonoBehaviour
{
    public float moveSpeed = 5f; // Player's horizontal movement speed
    public float jumpForce = 10f; // Jump force applied when the player jumps
    public LayerMask groundLayer; // Layer mask to define what is considered the ground
    public Transform groundCheck; // Position to check if the player is grounded
    public float groundCheckRadius = 0.2f; // Radius of the ground check

    private Rigidbody2D rb;
    private bool isGrounded = false;
    private float horizontalInput;

    void Start()
    {
        // Get the Rigidbody2D component for physics-based movement
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        // Get horizontal input (left or right movement)
        horizontalInput = Input.GetAxis("Horizontal");

        // Check if the player is grounded before jumping
        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

        // Jump when pressing space and grounded
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            Jump();
        }
    }

    void FixedUpdate()
    {
        // Apply horizontal movement to the player
        rb.linearVelocity = new Vector2(horizontalInput * moveSpeed, rb.linearVelocity.y);
    }

    // Method to apply jump force
    private void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
    }

    // Visualize the ground check radius in the scene view
    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
