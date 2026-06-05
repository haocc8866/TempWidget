# 一键推送到 GitHub

如果你这边能访问 GitHub, 运行这个脚本完成推送。

## 用法 (PowerShell)

```powershell
# 1. 克隆 bundle 到新目录
git clone "E:\项目\TempWidget\TempWidget.bundle" TempWidget-push
cd TempWidget-push
git checkout main

# 2. 用 token 推 (替换成你自己的 token)
git push https://ghp_<你的token>@github.com/haocc8866/TempWidget.git main:main
```

## 如果还是连不上 GitHub

试试以下任一:

1. **挂代理**:
   ```powershell
   $env:HTTPS_PROXY = "http://127.0.0.1:7890"  # 换成你的代理
   git push https://ghp_xxx@github.com/haocc8866/TempWidget.git main:main
   ```

2. **用 SSH 协议** (需要你本机有 SSH key 并加到 GitHub):
   ```powershell
   git remote set-url origin git@github.com:haocc8866/TempWidget.git
   git push -u origin main
   ```

3. **用 GitHub CLI** (推荐, 自动处理认证):
   ```powershell
   gh auth login --with-token  # 输入你的 token
   gh repo push                # 在 TempWidget-push 目录下
   ```

## 仓库地址
- https://github.com/haocc8866/TempWidget (已创建, 空的, 等 push)
