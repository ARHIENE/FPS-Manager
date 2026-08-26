using System;
using UnityEngine;

namespace FPSManager.Battle
{
    public class PlayerHealth : MonoBehaviour
    {
        [Header("팀")]
        public int teamId;

        [Header("체력")]
        public float maxHealth = 100f;

        public bool IsDead { get; private set; }
        public float CurrentHealth { get; private set; }
        public event Action<PlayerHealth> OnDeath;
        public event Action<PlayerHealth, PlayerHealth, bool> OnDeathWithAttacker;

        void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void ApplyDamage(float amount, PlayerHealth attacker = null, bool isHeadshot = false)
        {
            if (IsDead) return;

            CurrentHealth -= amount;
            if (CurrentHealth <= 0f)
            {
                Kill(attacker, isHeadshot);
            }
        }

        public void Kill(PlayerHealth attacker = null, bool isHeadshot = false)
        {
            if (IsDead) return;
            IsDead = true;
            CurrentHealth = 0f;

            var movement = GetComponent<PlayerMovement>();
            if (movement != null) movement.enabled = false;

            var weapon = GetComponent<WeaponController>();
            if (weapon != null) weapon.enabled = false;

            var brain = GetComponent<AIBrain>();
            if (brain != null) brain.enabled = false;

            var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                agent.isStopped = true;
                agent.enabled = false;
            }

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            OnDeath?.Invoke(this);
            OnDeathWithAttacker?.Invoke(this, attacker, isHeadshot);
        }
    }
}

