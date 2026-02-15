using System;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Treasure Chest — opens to reveal gold coins inside.
    /// </summary>
    public class TreasureChest : MonoBehaviour, IDescribable
    {
        [SerializeField] private bool isOpen = false;

        public ScreenFragment Describe()
        {
            var actions = isOpen
                ? null
                : new[] { GameAction.Create("Open", OpenChest, "Reveal what's inside") };

            var description = isOpen ? "The lid is wide open, gleaming gold coins visible inside" : "A wooden chest with iron bands. Closed.";

            return new ScreenFragment
            {
                Name = "Treasure Chest",
                Description = description,
                Actions = actions
            };
        }

        private string OpenChest()
        {
            isOpen = true;
            return "You open the chest — gold coins spill light across the floor.";
        }
    }

    /// <summary>
    /// Lever — pull to unlock the door.
    /// </summary>
    public class TreasureLever : MonoBehaviour, IDescribable
    {
        [SerializeField] private bool pulled = false;

        public bool IsPulled => pulled;

        public ScreenFragment Describe()
        {
            var actions = pulled
                ? null
                : new[] { GameAction.Create("Pull", PullLever, "See what happens") };

            var description = pulled ? "Lever is down. You hear a distant click." : "A rusty lever protruding from the wall.";

            return new ScreenFragment
            {
                Name = "Lever",
                Description = description,
                Actions = actions
            };
        }

        private string PullLever()
        {
            pulled = true;
            return "The lever resists, then gives way with a satisfying clunk. Something unlocked in the distance.";
        }
    }

    /// <summary>
    /// Door — locked until the lever is pulled. Opens to freedom.
    /// </summary>
    public class TreasureDoor : MonoBehaviour, IDescribable
    {
        [SerializeField] private bool isOpen = false;
        [SerializeField] private TreasureLever lever;

        private void Start()
        {
            // If lever not set, try to find it
            if (lever == null)
                lever = FindObjectOfType<TreasureLever>();
        }

        public ScreenFragment Describe()
        {
            GameAction[] actions;

            if (isOpen)
            {
                // Door is open — can go through
                actions = new[] { GameAction.Create("GoThrough", GoThrough, "Step into the light") };
            }
            else
            {
                // Door is closed — check if lever is pulled
                bool leverPulled = lever != null && lever.IsPulled;

                if (leverPulled)
                {
                    // Unlocked, can open
                    actions = new[] { GameAction.Create("Open", OpenDoor, "Push the heavy door") };
                }
                else
                {
                    // Locked
                    actions = new[] { GameAction.Disabled("Open", "The door is locked. You hear mechanism inside — something must unlock it.") };
                }
            }

            var description = isOpen
                ? "The door stands open. Sunlight streams through."
                : "A heavy wooden door with iron lock mechanism. " + (lever != null && lever.IsPulled ? "The lock is disengaged." : "Locked.");

            return new ScreenFragment
            {
                Name = "Door",
                Description = description,
                Actions = actions
            };
        }

        private string OpenDoor()
        {
            isOpen = true;
            return "The door swings open. Fresh air rushes in. You are free.";
        }

        private string GoThrough()
        {
            return "You step through the doorway into sunlight. The treasure room is behind you now.";
        }
    }
}
