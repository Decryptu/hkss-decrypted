# Host smoke test

Run this on a throwaway seed before inviting players. Use the launcher and `hkss.apworld` from the same release.

1. Follow the [setup checklist](https://decryptu.github.io/hkss-decrypted/setup.html) and use the [YAML generator](https://decryptu.github.io/hkss-decrypted/yaml.html) to make two YAMLs with the **Combo · Both goals** preset and different slot names. Generate a multiworld with a spoiler log (level 1 is enough) and host the ZIP.
2. With Steam running, choose an unused launcher run on each tester’s PC. In **Settings**, enter the host, port, slot name and password. Use **Test connection**, then **Save room**. Start both runs and confirm the host sees both slots connected.
3. Use the spoiler log to pick a reachable Hallownest check holding a Pharloom item for either combo slot. Collect it and confirm the host records the check and the item goes to the intended slot. When the recipient reaches Pharloom, confirm the item is there.
4. On one run, take the Dirtmouth gate to Pharloom. Reach Bone Bottom, use its gate to return to Dirtmouth and confirm previously collected checks stay collected. Close and reopen the run and check that it starts in the last kingdom.
5. If launch or travel fails, use **Retry run** to resume from the launcher. If it fails again, choose **Export bug report** and share the ZIP with the host along with the release version, kingdom, scene and action that failed.

Record each step as passed, failed or skipped. Use a fresh seed for the group session if the smoke test involved reading the spoiler log.
