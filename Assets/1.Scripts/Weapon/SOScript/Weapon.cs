using UnityEngine;

[CreateAssetMenu(fileName = "Weapon", menuName = "Scriptable Objects/Weapon")]
public class Weapon : ScriptableObject
{
    public string weaponId;
    public WeaponType weaponType;
    public string weaponName;

    public float baseDamage;            //공격력
    public float baseFireRate;          //연사속도
    public float baseReloadSpeed;       //재장전속도
    public float baseBulletSpray;       //탄퍼짐
    public float baseRecoil;            //반동수치
    public float baseAmmo;              //탄창 용량
    public float baseADSSpeed;          //조준속도
    public float baseNoiseLevel;        //소음량
    public float baseMobility;          //기동성

    public float baseBulletSpeed;       //탄환속도
}
