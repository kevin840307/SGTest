# SGTest 工具目錄（Clean-slate）

唯一正式素材入口為 [asset_converter/](asset_converter/README.md)，可直接透過儲存庫根目錄的 run_build_assets.bat 啟動。

- asset_converter/source：唯讀原始遊戲 SPR/ACT/SIF/Data1 資料解析
- asset_converter/atlas：單素材內打包與驗證
- asset_converter/content：Map/Blocking/Enemy/NPC/Player 部位、Runtime catalog 輸出
- asset_converter/benchmarks：保留 Recovered Model1 效能比較與像素驗證工具，不屬於 Runtime
- 根目錄少量 Python：原版格式研究與必要 Converter 依賴／Viewer
- tests/：純 Python 資產／研究 fixture，不需要可執行 Client/Server

**已移除**舊版 Client/Server Runtime 壓測、重複的合圖規劃／實驗工具及其專屬測試。原始程式仍可從 Git 歷史取回。

~~~powershell
python -m pip install -r requirements.txt
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
python -m unittest discover -s tools/tests -p "test_*.py" -q
~~~

權威當期規格：[docs/ASSET_CONVERTER.md](../docs/ASSET_CONVERTER.md)；新 Client/Server 尚未重建，不存在 Godot 主專案或 C# 可執行程式。
