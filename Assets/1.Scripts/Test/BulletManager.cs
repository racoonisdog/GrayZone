using UnityEngine;

public class BulletManager : MonoBehaviour
{
    private Rigidbody bulletRigidbody;

    [SerializeField]
    private float moveSpeed = 10f;

    [SerializeField]
    private float destroyTime = 3f;

    [SerializeField]
    private float damage = 1f;

    private float currentLifeTime;

    private void Start()
    {
        bulletRigidbody = GetComponent<Rigidbody>();
        currentLifeTime = destroyTime;
    }

    private void OnEnable()
    {
        currentLifeTime = destroyTime;
    }

    private void Update()
    {
        currentLifeTime -= Time.deltaTime;

        if (currentLifeTime <= 0f)
        {
            DestroyBullet();
            return;
        }

        BulletMove();
    }

    private void BulletMove()
    {
        if (bulletRigidbody != null)
        {
            bulletRigidbody.linearVelocity = transform.forward * moveSpeed;
        }
    }

    private void DestroyBullet()
    {
        gameObject.SetActive(false);
        currentLifeTime = destroyTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        Enemy enemy = other.GetComponentInParent<Enemy>();

        if (enemy != null)
        {
            Vector3 hitDirection = transform.forward;
            Vector3 hitPoint = other.ClosestPoint(transform.position);

            enemy.TakeDamage(damage, hitPoint, hitDirection);
        }

        DestroyBullet();
    }
}