# Execução na bandeja do sistema

**Data:** 19 de julho de 2026

## Objetivo

Quando já houver uma configuração válida, o Steam Import deve iniciar sem
manter uma janela visível na barra de tarefas. O servidor web e o lock de
instância continuam ativos, e a janela local fica disponível pelo ícone na
área de notificação do Windows.

## Fluxo público

- configuração válida: iniciar oculto, com ícone do Steam Import na bandeja;
- configuração ausente ou inválida: mostrar a janela guiada normalmente;
- duplo clique ou item **Abrir**: restaurar a janela e ativá-la;
- botão `X` ou **Fechar** da janela: ocultar novamente, sem parar o servidor;
- item **Sair**: permitir o fechamento real, parar o servidor e liberar o lock.

## Desenho

O estado de visibilidade e fechamento será encapsulado em um módulo pequeno e
testável, sem depender de WPF ou APIs nativas. A aplicação WPF usará esse
estado para coordenar `MainWindow` e um `NotifyIcon` do Windows Forms. O ícone
terá texto identificável e menu com **Abrir** e **Sair**; não haverá serviço do
Windows, tarefa agendada ou privilégio adicional.

O projeto WPF habilitará Windows Forms apenas para o `NotifyIcon`. O ícone
será criado depois do lock de instância e descartado no encerramento. A janela
será criada normalmente para executar o carregamento existente, mas será
ocultada imediatamente quando a retomada indicar configuração válida.

## Segurança e falhas

O tray não altera a porta HTTP, a proteção da chave SteamGridDB, o autostart
ou os dados persistidos. Se o ícone não puder ser criado, a aplicação registra
o erro e mantém a janela acessível, sem interromper o servidor. O item **Sair**
é o único caminho que chama o encerramento controlado da aplicação.

## Estratégia de testes

Os testes exercitam o módulo público de ciclo de vida com quatro
comportamentos verticais: início oculto, restauração, fechamento que volta a
ocultar e saída explícita. O build Release também deve compilar o WPF com
Windows Forms habilitado; as suítes existentes permanecem obrigatórias.
