#include <X11/Xlib.h>
#include <X11/Xutil.h>
#include <X11/Xatom.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/wait.h>
#include <signal.h>
#include <sys/prctl.h>

static void prop(Display *d, Window w, const char *name, Atom type, unsigned long value) {
    XChangeProperty(d,w,XInternAtom(d,name,False),type,32,PropModeReplace,(unsigned char *)&value,1);
}
static Window window(Display *d, const char *name, int pid, int x, int y, int width, int height, unsigned long color) {
    Window w=XCreateSimpleWindow(d,DefaultRootWindow(d),x,y,width,height,0,0,color);
    XStoreName(d,w,name);
    prop(d,w,"_NET_WM_PID",XA_CARDINAL,(unsigned long)pid);
    XMapWindow(d,w);XFlush(d);return w;
}
static void active(Display*d,Window w) {
    prop(d,DefaultRootWindow(d),"_NET_ACTIVE_WINDOW",XA_WINDOW,w);
    XSetInputFocus(d,w,RevertToPointerRoot,CurrentTime);XFlush(d);
}
static int compositor_pid(void) {
    Display*d=XOpenDisplay(NULL);if(!d)return 0;
    Atom type;int format;unsigned long count,left;unsigned char*data=NULL;int pid=0;
    XGetWindowProperty(d,DefaultRootWindow(d),XInternAtom(d,"GAMESCOPE_PID",False),0,1,False,XA_CARDINAL,&type,&format,&count,&left,&data);
    if(type==XA_CARDINAL&&format==32&&count==1)pid=(int)((unsigned long*)data)[0];
    if(data)XFree(data);
    XCloseDisplay(d);return pid;
}
static void ancestry(const char *fifo) {
    char path[1024];snprintf(path,sizeof path,"%s.ancestry",fifo);FILE*out=fopen(path,"w");if(!out)return;
    int pid=getpid();
    for(int depth=0;pid>1&&depth<12;depth++) {
        char comm[128]={0},command[1024]={0},status[256];int parent=0;
        snprintf(path,sizeof path,"/proc/%d/comm",pid);FILE*f=fopen(path,"r");if(f){if(!fgets(comm,sizeof comm,f))comm[0]=0;fclose(f);comm[strcspn(comm,"\n")]=0;}
        snprintf(path,sizeof path,"/proc/%d/cmdline",pid);f=fopen(path,"r");if(f){size_t length=fread(command,1,sizeof command-1,f);command[length]=0;fclose(f);}
        snprintf(path,sizeof path,"/proc/%d/status",pid);f=fopen(path,"r");if(f){while(fgets(status,sizeof status,f))if(sscanf(status,"PPid: %d",&parent)==1)break;fclose(f);}
        fprintf(out,"pid=%d parent=%d comm=%s executable=%s\n",pid,parent,comm,command);pid=parent;
    }
    fclose(out);
}
static int game(void) {
    prctl(PR_SET_NAME,"EliteDangerous6",0,0,0);
    Display*d=XOpenDisplay(NULL);if(!d)return 10;
    Window w=window(d,"Elite - Dangerous (CLIENT) TEST",getpid(),0,0,800,600,0xff0000);
    XClassHint klass={.res_name="EliteDangerous64.exe",.res_class="EliteDangerous64.exe"};XSetClassHint(d,w,&klass);
    prop(d,w,"STEAM_GAME",XA_CARDINAL,359320);XFlush(d);
    for(;;)pause();
}
static void list(Display*d,Window first,Window second,Window extra) {
    unsigned long values[3]={first,second,extra};
    XChangeProperty(d,DefaultRootWindow(d),XInternAtom(d,"_NET_CLIENT_LIST",False),XA_WINDOW,32,PropModeReplace,(unsigned char*)values,extra?3:2);
    XChangeProperty(d,DefaultRootWindow(d),XInternAtom(d,"_NET_CLIENT_LIST_STACKING",False),XA_WINDOW,32,PropModeReplace,(unsigned char*)values,extra?3:2);XFlush(d);
}
int main(int argc,char**argv) {
    setvbuf(stdout,NULL,_IOLBF,0);
    if(argc>1&&!strcmp(argv[1],"--game"))return game();
    if(argc!=3)return 2;
    Display*d=XOpenDisplay(getenv("HARNESS_OUTER_DISPLAY"));if(!d)return 11;
    int scope=0;for(int i=0;i<100&&!scope;i++){scope=compositor_pid();if(!scope)usleep(20000);}if(!scope)return 15;
    ancestry(argv[2]);
    Window outer=window(d,"gamescope synthetic outer (tracking only)",scope,100,80,1280,800,0x0033aa);
    Window other=window(d,"unrelated desktop app",getpid(),0,0,50,50,0x444444);
    Window duplicate=0;
    list(d,outer,other,0);active(d,outer);
    pid_t child=fork();if(!child){execl(argv[1],argv[1],"--game",NULL);_exit(12);}
    printf("HARNESS_READY scope=%d outer=0x%lx inner=%s game=%d\n",scope,outer,getenv("DISPLAY"),child);
    FILE*f=fopen(argv[2],"r");if(!f)return 13;
    char command[128];
    while(fgets(command,sizeof command,f)) {
        command[strcspn(command,"\n")]=0;
        if(!strcmp(command,"foreground"))active(d,outer);
        else if(!strcmp(command,"background"))active(d,other);
        else if(!strcmp(command,"move")){XMoveWindow(d,outer,320,200);XFlush(d);}
        else if(!strcmp(command,"resize")){XResizeWindow(d,outer,1000,700);XFlush(d);}
        else if(!strcmp(command,"hide")){XUnmapWindow(d,outer);active(d,other);}
        else if(!strcmp(command,"show")){XMapWindow(d,outer);active(d,outer);}
        else if(!strcmp(command,"hidden-state")){prop(d,outer,"_NET_WM_STATE",XA_ATOM,XInternAtom(d,"_NET_WM_STATE_HIDDEN",False));active(d,other);}
        else if(!strcmp(command,"clear-state")){XDeleteProperty(d,outer,XInternAtom(d,"_NET_WM_STATE",False));active(d,outer);}
        else if(!strcmp(command,"duplicate")){duplicate=window(d,"gamescope duplicate synthetic outer",scope,1600,100,300,200,0xaaaa00);list(d,outer,other,duplicate);}
        else if(!strcmp(command,"unduplicate")){if(duplicate)XDestroyWindow(d,duplicate);duplicate=0;list(d,outer,other,0);}
        else if(!strcmp(command,"quit"))break;
        else {printf("ERROR unknown command %s\n",command);return 14;}
        XSync(d,False);printf("ACK %s\n",command);
    }
    kill(child,SIGTERM);waitpid(child,NULL,0);
    XDestroyWindow(d,outer);XDestroyWindow(d,other);if(duplicate)XDestroyWindow(d,duplicate);XCloseDisplay(d);
    puts("HARNESS_DONE");return 0;
}
