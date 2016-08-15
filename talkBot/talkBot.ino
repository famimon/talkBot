#include <Servo.h>
Servo rightEyebrow;
Servo leftEyebrow;
Servo rightEye;
Servo rightEyelid;
Servo leftEye;
Servo leftEyelid;
Servo mouth;
#define R_EYEBROW_PIN 2
#define L_EYEBROW_PIN 4
#define R_EYEBALL_PIN 7
#define L_EYEBALL_PIN 8
#define R_EYELID_PIN 12
#define L_EYELID_PIN 13
#define MOUTH_PIN 11
#define MOUTH_CLED 3
#define MOUTH_RLED 5
#define MOUTH_LLED 6
#define EYEBROW_MIN 0
#define EYEBROW_MAX 70
#define EYEBALL_MIN 0
#define EYEBALL_MAX 100
#define EYELID_MIN 0
#define EYELID_MAX 120
#define MOUTH_MIN 0
#define MOUTH_MAX 20

int incomingByte; 
int posX,posY,d;
int v=0;

void setup()	 
{	   
  Serial.begin(9600);
  //Serial.begin(19200); // Serial communication with Visual Studio
  Serial.println("Ready");  
  pinMode(1, INPUT);
  for (int i=2; i<=13; i++){
    pinMode(i, OUTPUT);
  }
}	 

void loop()	
{	        
  if (Serial.available()) // check if data has been sent from the computer: 
  {  
    incomingByte = Serial.read();
    // Expecting a string with the format <<number>>'x'<<number>>'y'<<number>>'d'
    // coding x-coordinate, y-coordinate and distance to camera
    switch(incomingByte){
    case 'a':
      attachAll();
      break;
    case '0'...'9':         // receive value
      v=v*10 + incomingByte - '0';
      //Serial.println(v);
      break;
    case 'x':               // receive X coordinate
      posX=v;
      v=0;
      break;
    case 'y':               // receive Y coordinate
      posY=v;
      v=0;
      break;
    case 'd':               // receive distance
      d=v;
      v=0;
      // 'd' is the end of the message string, call moveEyes
      moveEyes();
      break;
    case 't':               // there's sound coming out of speaker      
      Talk();
      break;
    case 'k':
      detachAll();
      break;
    }
  }  
}	 

void moveEyes(){
  //Flush serial port
  Serial.flush();
  // keep eyeball coordinates in range
  int eyeballX = constrain(posX, EYEBALL_MIN, EYEBALL_MAX);
  int eyebrowX = constrain(posX, EYEBROW_MIN, EYEBROW_MAX);
  int eyelidY = constrain(posY, EYELID_MIN, EYELID_MAX);

  if (d < 1200){
    //increase elevation angle when getting closer to face
    int closeEyelidY = eyelidY + (1200-d);
    closeEyelidY = constrain(eyelidY, EYELID_MIN, EYELID_MAX);
    // cross eye when too close to face
    if (eyeballX > 50 && eyeballX < 70){   
      leftEye.write(eyeballX-20);
      rightEye.write(eyeballX+20);
      if(posY<50){
        leftEyebrow.write(eyebrowX);
        rightEyebrow.write(EYEBROW_MAX-eyebrowX);
      }
      else{
        leftEyebrow.write(EYEBROW_MAX-eyebrowX);
        rightEyebrow.write(eyebrowX);
      } 
    }
    else{
      leftEye.write(eyeballX);
      rightEye.write(eyeballX);
      leftEyebrow.write(eyebrowX);
      rightEyebrow.write(eyebrowX);
    }
  }  
  else {
    leftEye.write(eyeballX);
    rightEye.write(eyeballX);
    if (eyeballX > 50 && eyeballX < 70){   
      // int eyebrowY = map(posY, 40, 120, EYEBROW_MIN, EYEBROW_MAX);
      if(posY<50){
        leftEyebrow.write(eyebrowX);
        rightEyebrow.write(EYEBROW_MAX-eyebrowX);
      }
      else{
        leftEyebrow.write(EYEBROW_MAX-eyebrowX);
        rightEyebrow.write(eyebrowX);
      } 
    }
    else{
      leftEyebrow.write(eyebrowX);
      rightEyebrow.write(eyebrowX);
    }
  }
  leftEyelid.write(posY);
  rightEyelid.write(EYELID_MAX-posY); 
  // delay(300); 
}

void Talk(){
  Serial.flush();
  // open mouth
  mouth.attach(MOUTH_PIN);
  mouth.write(MOUTH_MIN);
  digitalWrite(MOUTH_CLED, HIGH);
  digitalWrite(MOUTH_LLED, HIGH);
  digitalWrite(MOUTH_RLED, HIGH);
  delay(300);
  //close mouth
  mouth.write(MOUTH_MAX);
  digitalWrite(MOUTH_CLED, LOW);
  digitalWrite(MOUTH_LLED, LOW);
  digitalWrite(MOUTH_RLED, LOW);
  delay(300);
  mouth.detach();
}

void attachAll(){
  rightEyebrow.attach(R_EYEBROW_PIN);
  leftEyebrow.attach(L_EYEBROW_PIN);
  rightEye.attach(R_EYEBALL_PIN);
  leftEye.attach(L_EYEBALL_PIN);
  rightEyelid.attach(R_EYELID_PIN);
  leftEyelid.attach(L_EYELID_PIN);
  rightEyelid.write(EYELID_MAX);
  leftEyelid.write(EYELID_MIN);
  rightEyebrow.write(EYEBROW_MIN);
  leftEyebrow.write(EYEBROW_MIN);  
}
void detachAll(){
  rightEyebrow.detach();
  leftEyebrow.detach();
  rightEye.detach();
  leftEye.detach();
  rightEyelid.detach();
  leftEyelid.detach();
  mouth.detach();
}

























