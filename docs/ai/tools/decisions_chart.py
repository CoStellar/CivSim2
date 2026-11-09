"""Draws docs/ai/img/decyzje_1000_tur.png from three decision logs (CivSim.Headless --decisions).

Usage: python3 docs/ai/tools/decisions_chart.py RUNS_DIR
RUNS_DIR holds why7.csv, why23.csv and why42.csv from runs on the standard map (since stage 6: 18000 ticks = 30 minutes at x1). Needs matplotlib.
"""
import os
import sys

RUNS = sys.argv[1] if len(sys.argv) > 1 else '.'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'decyzje_1000_tur.png')
import csv, collections, matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
names = {'SetStrategy':'Strateg: ustawia strategię','SetResearch':'Uczony: wybiera badanie','SetRecipe':'Skarbnik: zmienia recepturę',
 'MoveCamp':'Obóz przenosi się','SplitCamp':'Odłam zakłada nowy obóz','JoinCamp':'Mały obóz albo głodna wioska dołącza',
 'Settle':'Obóz osiada (wioska)','SendSettlers':'Wioska wysyła osadników','Explore':'Wyprawa w nieznane','Occupy':'Zajęcie pustej osady','Conquer':'Wypędzenie obcego obozu',
 'Journey':'Daleka wędrówka','Plunder':'Zabranie porzuconych dóbr',
 'ProposeTreaty':'Dyplomata: proponuje umowę','AcceptTreaty':'Dyplomata: przyjmuje','DeclineTreaty':'Dyplomata: odrzuca','DeclareWar':'Dyplomata: wypowiada wojnę',
 'LaunchCampaign':'Dowódca: wysyła kampanię','RecallCampaign':'Dowódca: odwołuje kampanię','ChangeGovernment':'Strateg: zmienia ustrój'}
order = list(names)
seeds=(7,23,42)
data={s:collections.Counter(r['action'] for r in csv.DictReader(open(f'{RUNS}/why{s}.csv'))) for s in seeds}
fig,(a,b)=plt.subplots(1,2,figsize=(15,7.5),gridspec_kw={'width_ratios':[1.1,1]})
cols=['#2a6fdb','#e8833a','#3aa676']
y=range(len(order))
h=0.27
for k,s in enumerate(seeds):
    vals=[data[s][o] for o in order]
    a.barh([i+(k-1)*h for i in y],vals,height=h,color=cols[k],label=f'seed {s}')
    for i,v in enumerate(vals):
        a.text(v+20,i+(k-1)*h,str(v),va='center',fontsize=7,color='#333')
a.set_yticks(list(y)); a.set_yticklabels([names[o] for o in order],fontsize=9); a.invert_yaxis()
a.set_title('Ile decyzji podjęło AI w 30 minut gry przy ×1 (mapa standard, 12 cywilizacji na starcie)',fontsize=10)
a.legend(frameon=False,fontsize=9); a.spines[['top','right']].set_visible(False); a.set_xlabel('liczba decyzji')
rows=list(csv.DictReader(open(f'{RUNS}/why7.csv')))
groups=[('Ruchy obozów',{'MoveCamp','Journey','Conquer','Occupy'},'#2a6fdb'),('Odłamy i łączenie',{'SplitCamp','JoinCamp'},'#9b59b6'),
 ('Osiedlanie i osadnicy',{'Settle','SendSettlers'},'#3aa676'),('Wyprawy',{'Explore'},'#16a085'),('Receptury',{'SetRecipe'},'#e8833a'),('Strategia, ustrój i badania',{'SetStrategy','ChangeGovernment','SetResearch','Plunder'},'#888888'),('Dyplomacja',{'ProposeTreaty','AcceptTreaty','DeclineTreaty','DeclareWar'},'#c0392b'),('Wojna (dowódca)',{'LaunchCampaign','RecallCampaign'},'#5d4037')]
last=max(int(r['turn']) for r in rows)
step=max(1,-(-last//10))
bins=list(range(0,step*10,step))
bottom=[0]*len(bins)
for label,acts,col in groups:
    v=[sum(1 for r in rows if r['action'] in acts and b0<int(r['turn'])<=b0+step) for b0 in bins]
    b.bar([f'{b0//600}–{(b0+step)//600} min' for b0 in bins],v,bottom=bottom,color=col,label=label)
    bottom=[x+y2 for x,y2 in zip(bottom,v)]
b.set_title('Seed 7: decyzje w kolejnych 3 minutach gry (×1)',fontsize=10); b.tick_params(axis='x',labelrotation=45,labelsize=8)
b.legend(frameon=False,fontsize=8); b.spines[['top','right']].set_visible(False); b.set_xlabel('minuty gry przy ×1 (600 taktów = 1 min)'); b.set_ylabel('liczba decyzji')
plt.tight_layout(); plt.savefig(OUT,dpi=130)
