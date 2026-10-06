# SVN Resource Baseline

This Git revision is paired with the following SVN resource baseline.

- Repository: `https://svna.gameinjae.kr/svn/GA7thFinal_Grayzone`
- Revision: `r351`
- SVN source root: `5. Projects`

Do not use SVN `HEAD` when reproducing this Git revision. Use `r351` for every
mapping below.

| SVN path | Unity project path |
| --- | --- |
| `5. Projects/3.Resources` | `Assets/3.Resources` |
| `5. Projects/3.Resources.meta` | `Assets/3.Resources.meta` |
| `5. Projects/4.ThirdParty` | `Assets/4.ThirdParty` |
| `5. Projects/4.ThirdParty.meta` | `Assets/4.ThirdParty.meta` |
| `5. Projects/RealToon` | `Assets/RealToon` |
| `5. Projects/RealToon.meta` | `Assets/RealToon.meta` |
| `5. Projects/FMODProject` | `FMODProject` |

## Clean Setup

Run these commands from the Git project root after cloning Git. The three
folders and the FMOD project are separate SVN working copies and are ignored by Git.

```powershell
$repo = "https://svna.gameinjae.kr/svn/GA7thFinal_Grayzone/5.%20Projects"
$revision = 351

svn checkout "$repo/3.Resources" "Assets/3.Resources" --revision $revision
svn checkout "$repo/4.ThirdParty" "Assets/4.ThirdParty" --revision $revision
svn checkout "$repo/RealToon" "Assets/RealToon" --revision $revision
svn checkout "$repo/FMODProject" "FMODProject" --revision $revision

svn export "$repo/3.Resources.meta" "Assets/3.Resources.meta" --revision $revision --force
svn export "$repo/4.ThirdParty.meta" "Assets/4.ThirdParty.meta" --revision $revision --force
svn export "$repo/RealToon.meta" "Assets/RealToon.meta" --revision $revision --force
```

Open Unity only after all seven mappings are present. Unity must not regenerate
the root `.meta` files because their GUIDs are part of the shared reference
contract. The FMOD checkout supplies the Studio project and built banks used by
the Unity FMOD integration.
