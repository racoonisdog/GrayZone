using UnityEngine;

public enum ModifierOp
{
    Add,
    Multiply,
    Minus,
    Division
}
public enum PartType
{
    Muzzle,                     // 총구: 소음기, 소염기, 컴펜세이
    Barrel,                     // 총열: 사거리나 이동 속도에 영향을 주는 다양한 길이의 배럴
    Laser,                      // 레이저: 지향 사격 정확도나 조준 속도를 높여주는 레이저 사이트
    Optic,                      // 조준경: 도트 사이트, 홀로그래픽, 고배율 스코프
    Stock,                      // 개머리판: 반동 제어나 기동성을 결정하는 부위
    Underbarrel,                // 하부 부착물: 수직/각진 손잡이, 유탄 발사기, 양각대
    Magazine,                   // 탄창: 대용량 탄창이나 특정 구역용 탄종 변경
    RearGrip,                   // 후방 그립: 손잡이 표면 처리를 통해 조준 안정성 개선
    Ammunition,                 // 탄약: 철갑탄, 저지력 탄환 등 탄의 속성 변경
}
public enum StatType
{
    Damage,            //공격력
    FireRate,          //연사속도
    ReloadSpeed,       //재장전속도
    BulletSpray,       //탄퍼짐
    Recoil,            //반동수치
    Ammo,              //탄창 용량
    ADSSpeed,          //조준속도
    NoiseLevel,        //소음량
    Mobility,          //기동성
    BulletSpeed,       //탄환속도
}
public enum WeaponType
{
    Pistol,             // 권총
    SubmachineGun,      // 기관단총 (SMG)
    AssaultRifle,       // 돌격소총 (AR)
    Shotgun,            // 산탄총
    SniperRifle,        // 저격소총 (SR)
    LightMachineGun,    // 경기관총 (LMG)
    Melee,              // 근접 무기
    Grenade             // 투척물
}