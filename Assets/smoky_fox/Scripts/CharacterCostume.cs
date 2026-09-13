using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterCostume : MonoBehaviour
{
    public Costume[] costumes;
    // Start is called before the first frame update
    void Start()
    {
        SetCostume(0);
    }

    public void SetCostume(int id)
    {
        foreach (Costume costume in costumes)
        {
            for (int i = 0; i < costume.meshes.Length; i++)
            {
                costume.meshes[i].SetActive(false);

            }
        }

        for (int j = 0; j < costumes[id].meshes.Length; j++)
        {
            costumes[id].meshes[j].SetActive(true);
            if (costumes[id].meshMaterials[j] != null)
            {
                costumes[id].meshes[j].GetComponent<SkinnedMeshRenderer>().sharedMaterial = costumes[id].meshMaterials[j];
            }
        }
    }
}
